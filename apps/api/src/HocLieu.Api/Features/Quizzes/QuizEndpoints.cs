using System.Net;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Attempts;
using HocLieu.Features.Documents;
using HocLieu.Features.Files;
using HocLieu.Infrastructure.Data;
using HocLieu.QuizImport;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Quizzes;

/// <summary>
/// Khu giáo viên: CRUD quiz + import Word/Excel + ẩn/hiện/hẹn giờ + kết quả/lượt làm (spec §5.2, §6, §10).
/// Khu công khai: giới thiệu quiz trước khi làm bài.
/// </summary>
public static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Danh sách bài tập của tôi =====
        app.MapGet("/api/teacher/quizzes", async (HttpContext ctx, AppDbContext db, TimeProvider time, CancellationToken ct) =>
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
            var query = db.Quizzes.IgnoreQueryFilters().Where(x => x.OwnerId == viewer.UserId);
            if (!includeDeleted)
                query = query.Where(x => !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(sectionSlug))
                query = query.Where(x => x.Section != null && x.Section.Slug == sectionSlug);
            if (grade is > 0 and < 6)
                query = query.Where(x => x.GradeId == grade);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var norm = Text.NormalizeForSearch(text);
                query = query.Where(x => x.SearchText != null && EF.Functions.ILike(x.SearchText, $"%{norm}%"));
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new MyQuizRow(
                    x.Id, x.Title, x.Slug,
                    x.Section == null ? null : x.Section.Slug,
                    x.Section == null ? null : x.Section.Name,
                    x.GradeId,
                    x.Subject == null ? null : x.Subject.Name,
                    x.SchoolYear == null ? null : x.SchoolYear.Name,
                    x.WeekNo,
                    x.Scope.ToString(), x.PublishMode.ToString(),
                    x.PublishFrom, x.PublishUntil,
                    Visibility.GetPublishState(x.PublishMode, x.PublishFrom, x.PublishUntil, now).ToString(),
                    x.ModerationStatus.ToString(),
                    x.QuestionCount, x.TotalPoints, x.AttemptCount,
                    x.ImportWarnings != null,
                    x.CreatedAt, x.UpdatedAt.ToString("o"), x.IsDeleted))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<MyQuizRow>(rows, total, page, pageSize));
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.list")
        .WithSummary("Bài tập của tôi (kể cả ẩn; includeDeleted=true để xem thùng rác)")
        .WithMetadata(new QueryParameter("section", "string"), new QueryParameter("grade", "integer"),
            new QueryParameter("q", "string"), new QueryParameter("includeDeleted", "boolean"),
            new QueryParameter("page", "integer"), new QueryParameter("pageSize", "integer"));

        // ===== Chi tiết (tabs editor /gv/bai-tap/:id) =====
        app.MapGet("/api/teacher/quizzes/{id:long}", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var detail = await svc.GetDetailAsync(viewer, id, ct);
            if (detail is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.Ok(detail);
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.get")
        .WithSummary("Chi tiết bài tập của tôi (câu hỏi + cài đặt + hiển thị)");

        // ===== Tạo (luôn Hidden) =====
        app.MapPost("/api/teacher/quizzes", async (CreateQuizRequest req, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var quiz = await svc.CreateAsync(viewer, req, ct);
                return Results.Created($"/api/teacher/quizzes/{quiz.Id}", new { id = quiz.Id });
            }
            catch (QuizValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.create")
        .WithSummary("Tạo bài tập trống (mặc định Đang ẩn)")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // ===== Sửa (thay toàn bộ; concurrency + cảnh báo chấm lại) =====
        app.MapPut("/api/teacher/quizzes/{id:long}", async (long id, UpdateQuizRequest req, string? confirmRegrade, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var quiz = await svc.UpdateAsync(viewer, id, req,
                    string.Equals(confirmRegrade, "true", StringComparison.OrdinalIgnoreCase), ct);
                if (quiz is null)
                    return ApiErrors.NotFound("Không tìm thấy bài tập");
                var detail = await svc.GetDetailAsync(viewer, id, ct);
                return Results.Ok(detail!);
            }
            catch (QuizValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (QuizConcurrencyException)
            {
                return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict,
                    "Bài tập đã được thay đổi. Hãy tải lại và thử lại.");
            }
            catch (QuizRegradeConflictException ex)
            {
                var extensions = new Dictionary<string, object?>
                {
                    ["code"] = ErrorCodes.QuizHasAttempts,
                    ["affectedAttempts"] = ex.AffectedAttempts,
                };
                return Results.Problem(
                    detail: null,
                    title: $"Đã có {ex.AffectedAttempts} lượt làm bài. Đổi đáp án/xóa câu/xóa phương án sẽ chấm lại các lượt đó — xác nhận?",
                    statusCode: 409,
                    type: $"https://hoclieu.dev/errors/{ErrorCodes.QuizHasAttempts}",
                    extensions: extensions);
            }
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.update")
        .WithSummary("Sửa bài tập (PUT thay toàn bộ; updatedAt lệch → 409; đổi đáp án khi có lượt làm → 409 + confirmRegrade=true)")
        .WithMetadata(new QueryParameter("confirmRegrade", "boolean"))
        .Produces(StatusCodes.Status409Conflict);

        // ===== Xóa mềm / khôi phục / nhân bản =====
        app.MapDelete("/api/teacher/quizzes/{id:long}", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var quiz = await svc.SoftDeleteAsync(viewer, id, ct);
            if (quiz is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.NoContent();
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.delete")
        .WithSummary("Xóa mềm (khôi phục trong 30 ngày)");

        app.MapPost("/api/teacher/quizzes/{id:long}/restore", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var quiz = await svc.RestoreAsync(viewer, id, ct);
            if (quiz is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.NoContent();
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.restore")
        .WithSummary("Khôi phục bài tập đã xóa");

        app.MapPost("/api/teacher/quizzes/{id:long}/duplicate", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var copy = await svc.DuplicateAsync(viewer, id, ct);
            if (copy is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.Created($"/api/teacher/quizzes/{copy.Id}", new { id = copy.Id });
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.duplicate")
        .WithSummary("Nhân bản bài tập về làm bản của mình")
        .Produces(StatusCodes.Status201Created);

        // ===== Hiện/Ẩn/Hẹn giờ (owner ∨ Lead|Deputy của tổ ∨ Admin) =====
        app.MapPatch("/api/teacher/quizzes/{id:long}/publish", async (long id, PublishRequest req, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            var (ok, code) = await svc.PublishAsync(viewer, id, req, ct);
            if (!ok)
                return code == "validation"
                    ? ApiErrors.Validation(new() { ["mode"] = new[] { "Chọn Hiện, Ẩn hoặc Hẹn giờ (kèm mốc thời gian)." } })
                    : ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.Ok(new { applied = true });
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.publish")
        .WithSummary("Đổi trạng thái hiển thị: { mode: Visible|Hidden|Scheduled, from?, until? }")
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // ===== Import Word/Excel → tạo quiz (Hidden) =====
        app.MapPost("/api/teacher/quizzes/import/docx", async (IFormFile file, long? sectionId, short? gradeId, short? weekNo, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var (quizId, warnings) = await svc.ImportAsync(viewer, file, sectionId, gradeId, weekNo, ct);
                return Results.Ok(new QuizImportResultDto(quizId, warnings));
            }
            catch (ImportException ex)
            {
                return ApiErrors.Problem((HttpStatusCode)ex.HttpStatus,
                    ex.HttpStatus == 415 ? ErrorCodes.FileInvalid : "file.too_large", ex.Message);
            }
            catch (FileUploadException ex)
            {
                return ApiErrors.Problem((HttpStatusCode)ex.Status, ErrorCodes.FileInvalid, ex.Message);
            }
        })
        .DisableAntiforgery()
        .RequireRateLimiting(RateLimits.Import)
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.importDocx")
        .WithSummary("Nhập quiz từ file Word (.docx)");

        app.MapPost("/api/teacher/quizzes/import/xlsx", async (IFormFile file, long? sectionId, short? gradeId, short? weekNo, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            if (viewer.UserId is null)
                return ApiErrors.Forbidden("Chưa xác thực");
            try
            {
                var (quizId, warnings) = await svc.ImportAsync(viewer, file, sectionId, gradeId, weekNo, ct);
                return Results.Ok(new QuizImportResultDto(quizId, warnings));
            }
            catch (ImportException ex)
            {
                return ApiErrors.Problem((HttpStatusCode)ex.HttpStatus,
                    ex.HttpStatus == 415 ? ErrorCodes.FileInvalid : "file.too_large", ex.Message);
            }
            catch (FileUploadException ex)
            {
                return ApiErrors.Problem((HttpStatusCode)ex.Status, ErrorCodes.FileInvalid, ex.Message);
            }
        })
        .DisableAntiforgery()
        .RequireRateLimiting(RateLimits.Import)
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.importXlsx")
        .WithSummary("Nhập quiz từ file Excel (.xlsx)");

        // ===== Kết quả & thống kê (spec §6.8) =====
        app.MapGet("/api/teacher/quizzes/{id:long}/attempts", async (long id, long? assignmentId, string? which, int? page, int? pageSize, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var quiz = await db.Quizzes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (quiz is null || (quiz.OwnerId != viewer.UserId && !viewer.IsAdmin))
                return ApiErrors.NotFound("Không tìm thấy bài tập");

            var (p, ps) = Pagination.Parse(page, pageSize);
            var (items, total) = await svc.ListAttemptsAsync(viewer, id, assignmentId, which, p, ps, ct);
            return Results.Ok(new PagedResult<AttemptRowDto>(items, total, p, ps));
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.attempts")
        .WithSummary("Bảng lượt làm (which: best|first|last, mặc định tất cả; lọc theo assignmentId)")
        .WithMetadata(new QueryParameter("assignmentId", "integer"), new QueryParameter("which", "string"),
            new QueryParameter("page", "integer"), new QueryParameter("pageSize", "integer"));

        app.MapDelete("/api/teacher/quizzes/{id:long}/attempts/{attemptId:guid}", async (long id, Guid attemptId, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var ok = await svc.DeleteAttemptAsync(viewer, id, attemptId, ct);
            return ok ? Results.NoContent() : ApiErrors.NotFound("Không tìm thấy lượt làm");
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.attemptDelete")
        .WithSummary("Xóa lượt làm rác");

        app.MapGet("/api/teacher/quizzes/{id:long}/stats", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var stats = await svc.GetStatsAsync(viewer, id, ct);
            if (stats is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.Ok(stats);
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.stats")
        .WithSummary("Thống kê: phân bố điểm, % đúng từng câu, phân bố phương án");

        app.MapGet("/api/teacher/quizzes/{id:long}/export.xlsx", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var exported = await svc.ExportAsync(viewer, id, ct);
            if (exported is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            var (bytes, fileName) = exported.Value;
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        })
        .RequireAuthorization("ActiveTeacher")
        .WithName("teacher.quizzes.export")
        .WithSummary("Xuất kết quả + thống kê câu ra Excel");

        // ===== Khu công khai: giới thiệu quiz (spec §5.1 /bai-tap/:slug-:id) =====
        app.MapGet("/api/public/quizzes/{id:long}", async (long id, AppDbContext db, QuizzesService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var dto = await svc.GetPublicDetailAsync(viewer, id, ctx.Request.Cookies["hl_dev"], ct);
            if (dto is null)
                return ApiErrors.NotFound("Không tìm thấy bài tập");
            return Results.Ok(dto);
        })
        .WithName("public.quiz")
        .WithSummary("Giới thiệu bài tập trước khi làm (không có câu hỏi)");

        return app;
    }
}
