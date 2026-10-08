using System.Net;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Files;

public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Giáo viên (ActiveTeacher) =====
        app.MapPost("/api/teacher/files", async (IFormFile file, AppDbContext db, FilesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var f = await svc.UploadAsync(file, viewer.UserId.Value, ct);
                return Results.Ok(FileDtos.Of(f));
            }
            catch (FileUploadException ex)
            {
                return ApiErrors.Problem(
                    (HttpStatusCode)ex.Status,
                    ex.Status == 415 ? ErrorCodes.FileInvalid : "file.too_large",
                    ex.Message);
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .DisableAntiforgery() // CSRF theo spec §3.4: header X-Requested-With qua CsrfMiddleware
        .WithName("teacher.files.upload")
        .WithSummary("Upload 1 file (multipart, field 'file'); Office vào hàng đợi preview qua Gotenberg");

        app.MapGet("/api/teacher/files/{id:long}", async (long id, AppDbContext db, FilesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");

            var (f, _) = await svc.GetWithDocumentAsync(id, ct);
            if (f is null || (f.OwnerId != viewer.UserId && !viewer.IsAdmin))
                return ApiErrors.NotFound("Không tìm thấy file");
            return Results.Ok(FileDtos.Of(f));
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.files.status")
        .WithSummary("Trạng thái xử lý preview của file (FE poll 2s/lần)");

        // ===== Công khai (quyền theo tài liệu chứa file, spec §11.3) =====
        app.MapGet("/api/files/{id:long}/pages", async (long id, HttpContext ctx, AppDbContext db, FilesService svc, CancellationToken ct) =>
        {
            var from = int.TryParse(ctx.Request.Query["from"], out var f) ? f : 1;
            var count = int.TryParse(ctx.Request.Query["count"], out var c) ? c : 10;
            if (from < 1) from = 1;
            if (count is < 1 or > 50) count = 10;

            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var pages = await svc.GetPagesAsync(id, viewer, from, count, ct);
            return pages is null ? ApiErrors.NotFound("Không tìm thấy file") : Results.Ok(pages);
        })
        .WithName("public.files.pages")
        .WithSummary("URL preview từng trang (khách chỉ xem tài liệu công khai đang hiện)");

        app.MapGet("/api/files/{id:long}/download", async (long id, AppDbContext db, FilesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var result = await svc.GetDownloadAsync(id, viewer, ct);
            if (result is null)
                return ApiErrors.NotFound("Không tìm thấy file");

            var (file, _, url) = result.Value;
            if (url is not null)
                return Results.Redirect(url); // Cloudinary: 302 sang URL ký 10 phút

            if (svc.Storage is LocalDiskFileStorage local)
            {
                var path = local.ResolveOriginalPath(file);
                if (path is null)
                    return ApiErrors.NotFound("File chưa sẵn sàng");
                var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
                return Results.File(bytes, file.Mime, file.OriginalName);
            }
            return ApiErrors.NotFound("File chưa sẵn sàng");
        })
        .WithName("public.files.download")
        .WithSummary("Tải file; khách chỉ khi tài liệu cho phép (allow_guest_download)");

        // ===== Ảnh nhúng trong câu hỏi quiz (spec §6.3.11) =====
        app.MapGet("/api/files/{id:long}/image", async (long id, HttpContext ctx, AppDbContext db, FilesService svc, TimeProvider time, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (file is null || file.QuizId is null)
                return ApiErrors.NotFound("Không tìm thấy ảnh");

            var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == file.QuizId, ct);
            if (quiz is null || !Visibility.QuizVisibleTo(quiz, viewer, time.GetUtcNow()))
                return ApiErrors.NotFound("Không tìm thấy ảnh");

            var url = await svc.Storage.GetImageUrlAsync(file, ct);
            if (url is not null)
                return Results.Redirect(url); // Cloudinary: 302 sang URL ký

            var bytes = await svc.Storage.GetOriginalBytesAsync(file, ct);
            if (bytes is null)
                return ApiErrors.NotFound("Ảnh chưa sẵn sàng");
            ctx.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(bytes, file.Mime);
        })
        .WithName("public.files.image")
        .WithSummary("Ảnh trong câu hỏi quiz (quyền theo quiz chứa; quiz ẩn → 404)");

        // Stream preview chế độ local — token DataProtection 10 phút (decisions.md M3)
        app.MapGet("/api/files/{id:long}/preview", async (long id, string? token, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            if (storage is not LocalDiskFileStorage local)
                return Results.NotFound(); // Cloudinary: FE dùng URL ký từ /pages

            var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null)
                return Results.NotFound();

            var preview = local.ResolvePreviewPath(f, token);
            if (preview is null || !System.IO.File.Exists(preview.Value.Path))
                return Results.NotFound();
            var previewBytes = await System.IO.File.ReadAllBytesAsync(preview.Value.Path, ct);
            return Results.File(previewBytes, FileTypes.MimeOf(preview.Value.Ext));
        })
        .WithName("public.files.preview")
        .WithSummary("Stream preview (chế độ local): PDF/ảnh/video theo token 10 phút");

        return app;
    }
}
