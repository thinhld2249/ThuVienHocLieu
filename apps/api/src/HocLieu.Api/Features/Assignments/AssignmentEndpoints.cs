using System.Net;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Features.Attempts;
using HocLieu.Features.Classes;
using HocLieu.Infrastructure.Data;

namespace HocLieu.Features.Assignments;

/// <summary>
/// §7, §10: giao bài cho lớp bằng mã 6 ký tự (link + QR gửi nhóm Zalo phụ huynh).
/// Công khai: GET mã → thông tin (+ roster chỉ khi mở); POST mã → tạo attempt
/// (không phụ thuộc trạng thái ẩn/hiện của quiz).
/// </summary>
public static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== GV: bài đã giao của lớp =====

        app.MapGet("/api/teacher/classes/{id:long}/assignments", async (long id, AppDbContext db, AssignmentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var list = await svc.ListForClassAsync(viewer, id, ct);
                return Results.Ok(list);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.assignments.list")
        .WithSummary("Bài đã giao của lớp (kèm số lượt làm, điểm cao nhất)")
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/teacher/classes/{id:long}/assignments", async (long id, CreateAssignmentRequest req, AppDbContext db, AssignmentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var dto = await svc.CreateAsync(viewer, id, req, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp");
                return Results.Created($"/api/teacher/classes/{id}/assignments/{dto.Id}", dto);
            }
            catch (AssignmentValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.assignments.create")
        .WithSummary("Giao bài cho lớp (sinh mã 6 ký tự; giờ mở/đóng tùy chọn)")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapPut("/api/teacher/classes/{id:long}/assignments/{assignmentId:long}", async (long id, long assignmentId, UpdateAssignmentRequest req, AppDbContext db, AssignmentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var dto = await svc.UpdateAsync(viewer, id, assignmentId, req, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy bài giao");
                return Results.Ok(dto);
            }
            catch (AssignmentValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.assignments.update")
        .WithSummary("Đổi giờ mở/đóng, chế độ danh sách lớp (null = giữ nguyên)")
        .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/api/teacher/classes/{id:long}/assignments/{assignmentId:long}", async (long id, long assignmentId, AppDbContext db, AssignmentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            try
            {
                var ok = await svc.DeleteAsync(viewer, id, assignmentId, ct);
                if (!ok)
                    return ApiErrors.NotFound("Không tìm thấy bài giao");
                return Results.NoContent();
            }
            catch (ClassForbiddenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Forbidden, ErrorCodes.ClassNoAccess, "Bạn không có quyền với lớp này.");
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.assignments.delete")
        .WithSummary("Xóa bài giao (XÓA CỨNG kèm lượt làm) — cần xác nhận phía FE")
        .Produces(StatusCodes.Status404NotFound);

        // ===== Công khai: mã giao bài (spec §7) =====

        app.MapGet("/api/public/assignments/{code}", async (string code, HttpContext ctx, AppDbContext db, AssignmentsService svc, CancellationToken ct) =>
        {
            var device = AttemptEndpoints.GetOrCreateDevice(ctx);
            var dto = await svc.GetPublicAsync(code, device, ct);
            if (dto is null)
                return ApiErrors.NotFound("Không tìm thấy mã giao bài");
            return Results.Ok(dto);
        })
        .WithName("public.assignment.get")
        .WithSummary("Thông tin bài giao theo mã (roster chỉ khi đang mở & dùng danh sách lớp)")
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/public/assignments/{code}/attempts", async (
            string code, CreateAssignmentAttemptRequest? req, HttpContext ctx, AppDbContext db,
            AssignmentsService svc, IConfiguration config, CancellationToken ct) =>
        {
            var device = AttemptEndpoints.GetOrCreateDevice(ctx);
            var ipHash = AttemptEndpoints.HashIp(ctx, config);
            try
            {
                var dto = await svc.CreateAttemptAsync(code, req, device, ipHash, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy mã giao bài");
                return Results.Ok(dto);
            }
            catch (AssignmentNotOpenException)
            {
                return ApiErrors.Problem(HttpStatusCode.Conflict, "assignment.not_open", "Bài giao chưa mở. Hãy quay lại sau giờ mở.");
            }
            catch (AssignmentClosedException)
            {
                return ApiErrors.Problem(HttpStatusCode.Conflict, "assignment.closed", "Bài giao đã đóng.");
            }
            catch (AttemptValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (AttemptLimitException)
            {
                return ApiErrors.Problem(HttpStatusCode.UnprocessableEntity, "attempt.limit",
                    "Bạn đã dùng hết số lượt làm bài.");
            }
        })
        .RequireRateLimiting(RateLimits.CreateAttempt)
        .WithName("public.assignment.attempt.create")
        .WithSummary("Tạo lượt làm qua mã giao bài (không phụ thuộc ẩn/hiện của quiz)")
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
