using System.Net;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Documents;

/// <summary>Khu giáo viên: CRUD tài liệu + ẩn/hiện/hẹn giờ + yêu thích (spec §5.2, §10).</summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Danh sách tài liệu của tôi =====
        app.MapGet("/api/teacher/documents", async (HttpContext ctx, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");

            var q = ctx.Request.Query;
            var sectionSlug = q["section"].ToString();
            var grade = short.TryParse(q["grade"], out var g) ? g : (short?)null;
            var text = q["q"].ToString();
            var includeDeleted = string.Equals(q["includeDeleted"], "true", StringComparison.OrdinalIgnoreCase);
            var (page, pageSize) = Pagination.Parse(
                int.TryParse(q["page"], out var p) ? p : null,
                int.TryParse(q["pageSize"], out var ps) ? ps : null);

            var now = time.GetUtcNow();
            var query = db.Documents.IgnoreQueryFilters()
                .Where(d => d.OwnerId == viewer.UserId);
            if (!includeDeleted)
                query = query.Where(d => !d.IsDeleted);
            if (!string.IsNullOrWhiteSpace(sectionSlug))
                query = query.Where(d => d.Section != null && d.Section.Slug == sectionSlug);
            if (grade is > 0 and < 6)
                query = query.Where(d => d.GradeId == grade);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var norm = Text.NormalizeForSearch(text);
                query = query.Where(d => d.SearchText != null && EF.Functions.ILike(d.SearchText, $"%{norm}%"));
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new MyDocumentRow(
                    d.Id, d.Title, d.Slug,
                    d.Section == null ? null : d.Section.Slug,
                    d.Section == null ? null : d.Section.Name,
                    d.GradeId,
                    d.Subject == null ? null : d.Subject.Name,
                    d.SchoolYear == null ? null : d.SchoolYear.Name,
                    d.WeekNo,
                    d.Scope.ToString(), d.PublishMode.ToString(),
                    d.PublishFrom, d.PublishUntil,
                    Visibility.GetPublishState(d.PublishMode, d.PublishFrom, d.PublishUntil, now).ToString(),
                    d.ModerationStatus.ToString(),
                    d.Files.Count, d.ViewCount, d.DownloadCount,
                    d.CreatedAt, d.UpdatedAt.ToString("o"), d.IsDeleted))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<MyDocumentRow>(rows, total, page, pageSize));
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.list")
        .WithSummary("Tài liệu của tôi (kể cả ẩn; includeDeleted=true để xem thùng rác)")
        .WithMetadata(new QueryParameter("section", "string"), new QueryParameter("grade", "integer"),
            new QueryParameter("q", "string"), new QueryParameter("includeDeleted", "boolean"),
            new QueryParameter("page", "integer"), new QueryParameter("pageSize", "integer"));

        // ===== Chi tiết =====
        app.MapGet("/api/teacher/documents/{id:long}", async (long id, AppDbContext db, TimeProvider time, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var doc = await LoadFullAsync(db, id, ct);
            if (doc is null || (doc.OwnerId != viewer.UserId && !viewer.IsAdmin))
                return ApiErrors.NotFound("Không tìm thấy tài liệu");
            return Results.Ok(MapDetail(doc, time.GetUtcNow()));
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.get")
        .WithSummary("Chi tiết tài liệu của tôi");

        // ===== Tạo =====
        app.MapPost("/api/teacher/documents", async (CreateDocumentRequest req, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var doc = await svc.CreateAsync(viewer, req, ct);
                return Results.Created($"/api/teacher/documents/{doc.Id}", new { id = doc.Id });
            }
            catch (DocumentValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.create")
        .WithSummary("Tạo tài liệu (file đã upload qua /api/teacher/files)")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // ===== Sửa (thay toàn bộ + concurrency) =====
        app.MapPut("/api/teacher/documents/{id:long}", async (long id, UpdateDocumentRequest req, AppDbContext db, DocumentsService svc, TimeProvider time, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var doc = await svc.UpdateAsync(viewer, id, req, ct);
                if (doc is null)
                    return ApiErrors.NotFound("Không tìm thấy tài liệu");
                var full = await LoadFullAsync(db, id, ct);
                return Results.Ok(MapDetail(full!, time.GetUtcNow()));
            }
            catch (DocumentValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (DocumentConcurrencyException)
            {
                return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict,
                    "Tài liệu đã được thay đổi. Hãy tải lại và thử lại.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.update")
        .WithSummary("Sửa tài liệu (PUT thay toàn bộ; updatedAt lệch → 409)");

        // ===== Xóa mềm / khôi phục / nhân bản =====
        app.MapDelete("/api/teacher/documents/{id:long}", async (long id, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var doc = await svc.SoftDeleteAsync(viewer, id, ct);
            if (doc is null)
                return ApiErrors.NotFound("Không tìm thấy tài liệu");
            return Results.NoContent();
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.delete")
        .WithSummary("Xóa mềm (khôi phục trong 30 ngày)");

        app.MapPost("/api/teacher/documents/{id:long}/restore", async (long id, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var doc = await svc.RestoreAsync(viewer, id, ct);
            if (doc is null)
                return ApiErrors.NotFound("Không tìm thấy tài liệu");
            return Results.NoContent();
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.restore")
        .WithSummary("Khôi phục tài liệu đã xóa");

        app.MapPost("/api/teacher/documents/{id:long}/duplicate", async (long id, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var copy = await svc.DuplicateAsync(viewer, id, ct);
            if (copy is null)
                return ApiErrors.NotFound("Không tìm thấy tài liệu");
            return Results.Created($"/api/teacher/documents/{copy.Id}", new { id = copy.Id });
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.duplicate")
        .WithSummary("Nhân bản tài liệu về làm bản của mình")
        .Produces(StatusCodes.Status201Created);

        // ===== Hiện/Ẩn/Hẹn giờ (owner ∨ Lead|Deputy của tổ ∨ Admin) =====
        app.MapPatch("/api/teacher/documents/{id:long}/publish", async (long id, PublishRequest req, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            var (ok, code) = await svc.PublishAsync(viewer, id, req, ct);
            if (!ok)
                return code == "validation"
                    ? ApiErrors.Validation(new() { ["mode"] = new[] { "Chọn Hiện, Ẩn hoặc Hẹn giờ (kèm mốc thời gian)." } })
                    : ApiErrors.NotFound("Không tìm thấy tài liệu");
            return Results.Ok(new { applied = true });
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.documents.publish")
        .WithSummary("Đổi trạng thái hiển thị: { mode: Visible|Hidden|Scheduled, from?, until? }")
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/teacher/content/publish-bulk", async (BulkPublishRequest req, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            var result = await svc.PublishBulkAsync(viewer, req, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.content.publishBulk")
        .WithSummary("Thao tác hàng loạt: Hiện/Ẩn/Hẹn giờ nhiều nội dung");

        // ===== Yêu thích =====
        app.MapGet("/api/teacher/favorites", async (AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            return Results.Ok(await svc.ListFavoritesAsync(viewer.UserId.Value, ct));
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.favorites.list")
        .WithSummary("Tài liệu/bài tập đã lưu");

        app.MapPost("/api/teacher/favorites", async (FavoriteRequest req, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            var type = (req.ItemType ?? string.Empty).ToLowerInvariant() == "quiz"
                ? FavoriteItemType.Quiz
                : FavoriteItemType.Document;
            var ok = await svc.AddFavoriteAsync(viewer, type, req.ItemId ?? 0, ct);
            return ok ? Results.NoContent() : ApiErrors.NotFound("Không tìm thấy nội dung");
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.favorites.add")
        .WithSummary("Lưu tài liệu/bài tập vào Yêu thích")
        .Produces(StatusCodes.Status204NoContent);

        app.MapDelete("/api/teacher/favorites/{itemType}/{itemId:long}", async (string itemType, long itemId, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            var type = itemType.ToLowerInvariant() == "quiz" ? FavoriteItemType.Quiz : FavoriteItemType.Document;
            await svc.RemoveFavoriteAsync(viewer.UserId.Value, type, itemId, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.favorites.remove")
        .WithSummary("Bỏ yêu thích");

        return app;
    }

    private static async Task<Document?> LoadFullAsync(AppDbContext db, long id, CancellationToken ct)
        => await db.Documents.IgnoreQueryFilters()
            .Include(d => d.Section).Include(d => d.Grade).Include(d => d.Subject)
            .Include(d => d.SchoolYear).Include(d => d.Team).Include(d => d.Owner)
            .Include(d => d.Files).ThenInclude(f => f.File)
            .Include(d => d.Tags).ThenInclude(t => t.Tag)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    internal static DocumentDetailDto MapDetail(Document doc, DateTimeOffset now)
        => new(
            doc.Id, doc.Title, doc.Slug, doc.Summary, doc.DescriptionHtml,
            doc.SectionId, doc.Section?.Slug, doc.Section?.Name,
            doc.GradeId, doc.Grade?.Name,
            doc.SubjectId, doc.Subject?.Name,
            doc.SchoolYearId, doc.SchoolYear?.Name,
            doc.WeekNo,
            doc.TeamId, doc.Team?.Name,
            doc.OwnerId, doc.Owner?.FullName,
            doc.Scope.ToString(), doc.PublishMode.ToString(),
            doc.PublishFrom, doc.PublishUntil,
            Visibility.GetPublishState(doc.PublishMode, doc.PublishFrom, doc.PublishUntil, now).ToString(),
            doc.ModerationStatus.ToString(), doc.ModerationNote,
            doc.AllowGuestDownload,
            doc.CoverFileId,
            doc.Files.OrderBy(f => f.Sort).Select(f => Files.FileDtos.Of(f.File)).ToList(),
            doc.Tags.Select(t => new TagRef(t.TagId, t.Tag.Name)).OrderBy(t => t.Name).ToList(),
            doc.IsDeleted,
            doc.CreatedAt, doc.UpdatedAt.ToString("o"),
            doc.ViewCount, doc.DownloadCount);
}
