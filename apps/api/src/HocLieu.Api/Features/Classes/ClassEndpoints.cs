using System.Net;
using ClosedXML.Excel.Exceptions;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Infrastructure.Data;

namespace HocLieu.Features.Classes;

/// <summary>
/// §7 + §10: lớp học & học sinh của GV.
/// Quyền truy cập: CanAccessClass (chủ nhiệm ∨ GV bộ môn ∨ Lead|Deputy của tổ ∨ Admin).
/// Xóa lớp/học sinh = xóa cứng (kèm lượt làm) — FE bắt buộc xác nhận trước khi gọi.
/// </summary>
public static class ClassEndpoints
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapClassEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Lớp =====

        app.MapGet("/api/teacher/classes", async (AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var list = await svc.ListMineAsync(viewer, ct);
            return Results.Ok(list);
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.classes.list")
        .WithSummary("Danh sách lớp của tôi (chủ nhiệm ∨ GV bộ môn ∨ tổ mình; Admin: tất cả)");

        app.MapPost("/api/teacher/classes", async (CreateClassRequest req, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var created = await svc.CreateAsync(viewer, req, ct);
                return Results.Created($"/api/teacher/classes/{created.Id}", created);
            }
            catch (ClassValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.classes.create")
        .WithSummary("Tạo lớp (GV chủ nhiệm = người tạo; tổ/năm học mặc định hợp lệ nhất)")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/teacher/classes/{id:long}", async (long id, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var dto = await svc.GetDtoAsync(viewer, id, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                return Results.Ok(dto);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.classes.get")
        .WithSummary("Chi tiết lớp")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPut("/api/teacher/classes/{id:long}", async (long id, UpdateClassRequest req, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var dto = await svc.UpdateAsync(viewer, id, req, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                return Results.Ok(dto);
            }
            catch (ClassValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.classes.update")
        .WithSummary("Sửa lớp (tên, khối, năm học, tổ, GV bộ môn; GV chủ nhiệm chỉ đổi được ở Admin)")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/api/teacher/classes/{id:long}", async (long id, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var ok = await svc.DeleteAsync(viewer, id, ct);
                if (!ok)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                return Results.NoContent();
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.classes.delete")
        .WithSummary("Xóa lớp (XÓA CỨNG kèm học sinh, bài đã giao, lượt làm) — cần xác nhận phía FE")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ===== Học sinh =====

        app.MapGet("/api/teacher/classes/{id:long}/students", async (long id, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var list = await svc.ListStudentsAsync(viewer, id, ct);
                return Results.Ok(list);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.students.list")
        .WithSummary("Danh sách học sinh (kể cả đang ngừng hoạt động)")
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/teacher/classes/{id:long}/students", async (long id, CreateStudentRequest req, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var created = await svc.CreateStudentAsync(viewer, id, req, ct);
                return Results.Created($"/api/teacher/classes/{id}/students/{created.Id}", created);
            }
            catch (ClassValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
            catch (ClassNotFoundException)
            {
                return ApiErrors.NotFound("Không tìm thấy lớp");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.students.create")
        .WithSummary("Thêm học sinh thủ công")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapPut("/api/teacher/classes/{id:long}/students/{studentId:long}", async (long id, long studentId, UpdateStudentRequest req, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var dto = await svc.UpdateStudentAsync(viewer, id, studentId, req, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy học sinh");
                return Results.Ok(dto);
            }
            catch (ClassValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.students.update")
        .WithSummary("Sửa học sinh (tên, mã HS, ngày sinh, giới tính, thứ tự, hoạt động)")
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/api/teacher/classes/{id:long}/students/{studentId:long}", async (long id, long studentId, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var ok = await svc.DeleteStudentAsync(viewer, id, studentId, ct);
                if (!ok)
                    return ApiErrors.NotFound("Không tìm thấy học sinh");
                return Results.NoContent();
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.students.delete")
        .WithSummary("Xóa học sinh (XÓA CỨNG kèm lượt làm) — cần xác nhận phía FE")
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/teacher/classes/{id:long}/students/import", async (long id, IFormFile file, string? dryRun, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            if (file is null || file.Length == 0)
                return ApiErrors.Validation(new() { ["file"] = ["Hãy chọn file .xlsx (tải mẫu để điền)."] });

            var preview = string.Equals(dryRun, "true", StringComparison.OrdinalIgnoreCase);
            try
            {
                using var stream = file.OpenReadStream();
                var result = await svc.ImportStudentsAsync(viewer, id, stream, preview, ct);
                return Results.Ok(result);
            }
            catch (ClassValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
            catch (ClosedXMLException)
            {
                return ApiErrors.Problem(HttpStatusCode.UnsupportedMediaType, ErrorCodes.FileInvalid,
                    "File không đọc được. Hãy lưu file dưới định dạng .xlsx rồi tải lên lại.");
            }
        })
        .DisableAntiforgery()
        .RequireRateLimiting(RateLimits.Import)
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.students.import")
        .WithSummary("Nhập danh sách HS từ Excel (dryRun=true: xem trước, không ghi)")
        .WithMetadata(new QueryParameter("dryRun", "boolean"))
        .Produces(StatusCodes.Status415UnsupportedMediaType)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/teacher/classes/{id:long}/students/export.xlsx", async (long id, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var cls = await svc.GetCheckedAsync(viewer, id, ct);
                if (cls is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                var bytes = await svc.ExportStudentsAsync(viewer, id, ct);
                return Results.File(bytes, XlsxMime, $"danh-sach-hs-{Slugify.ToSlug(cls.Name)}.xlsx");
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.students.export")
        .WithSummary("Xuất danh sách HS đang học ra Excel");

        // ===== Bảng điểm =====

        app.MapGet("/api/teacher/classes/{id:long}/gradebook", async (long id, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var gb = await svc.GetGradebookAsync(viewer, id, ct);
                if (gb is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                return Results.Ok(gb);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.gradebook")
        .WithSummary("Bảng điểm: hàng = HS đang học, cột = bài đã giao, ô = điểm cao nhất (null = chưa làm)")
        .Produces(StatusCodes.Status403Forbidden);

        app.MapGet("/api/teacher/classes/{id:long}/gradebook.xlsx", async (long id, AppDbContext db, ClassesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var cls = await svc.GetCheckedAsync(viewer, id, ct);
                if (cls is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                var bytes = await svc.ExportGradebookAsync(viewer, id, ct);
                return Results.File(bytes, XlsxMime, $"bang-diem-{Slugify.ToSlug(cls.Name)}.xlsx");
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.gradebook.export")
        .WithSummary("Xuất bảng điểm ra Excel");

        return app;
    }
}
