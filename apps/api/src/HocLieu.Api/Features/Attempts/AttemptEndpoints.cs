using System.Net;
using System.Security.Cryptography;
using System.Text;
using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Infrastructure.Data;

namespace HocLieu.Features.Attempts;

/// <summary>
/// §6.6: làm bài của khách (không cần tài khoản). Khóa truy cập = uuid v7 của attempt.
/// Thiết bị nhận dạng bằng cookie <c>hl_dev</c> (tạo server nếu chưa có).
/// </summary>
public static class AttemptEndpoints
{
    private const string DeviceCookie = "hl_dev";

    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Bắt đầu làm bài =====
        app.MapPost("/api/public/quizzes/{id:long}/attempts", async (
            long id, CreateAttemptRequest? req, HttpContext ctx, AppDbContext db, AttemptsService svc,
            IConfiguration config, CancellationToken ct) =>
        {
            var device = GetOrCreateDevice(ctx);
            var ipHash = HashIp(ctx, config);

            try
            {
                var dto = await svc.CreateAsync(id, device, req, ipHash, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy bài tập");
                return Results.Ok(dto);
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
        .WithName("public.attempt.create")
        .WithSummary("Tạo lượt làm bài (quiz phải đang mở; nhận diện theo identity_mode)")
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // ===== Xem attempt: làm tiếp (InProgress) hoặc kết quả =====
        app.MapGet("/api/public/attempts/{id:guid}", async (Guid id, AppDbContext db, AttemptsService svc, CancellationToken ct) =>
        {
            var dto = await svc.GetAsync(id, ct);
            if (dto is null)
                return ApiErrors.NotFound("Không tìm thấy lượt làm bài");
            return Results.Ok(dto);
        })
        .WithName("public.attempt.get")
        .WithSummary("Lượt làm theo uuid (câu hỏi + câu trả lời đã lưu; không chứa đáp án đúng)");

        // ===== Lưu câu trả lời (FE debounce 1,5s) =====
        app.MapPut("/api/public/attempts/{id:guid}/answers", async (
            Guid id, IReadOnlyList<SaveAnswerItem> items, AppDbContext db, AttemptsService svc, CancellationToken ct) =>
        {
            try
            {
                var saved = await svc.SaveAnswersAsync(id, items, ct);
                return Results.Ok(new { saved = saved.Count });
            }
            catch (AttemptNotFoundException ex)
            {
                return ApiErrors.NotFound(ex.Message);
            }
            catch (AttemptValidationException ex)
            {
                return ApiErrors.Validation(ex.Errors);
            }
            catch (AttemptConflictException ex)
            {
                return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, ex.Message);
            }
            catch (AttemptGoneException ex)
            {
                return ApiErrors.Problem(HttpStatusCode.Gone, "attempt.gone", ex.Message);
            }
        })
        .RequireRateLimiting(RateLimits.SaveAnswers)
        .WithName("public.attempt.saveAnswers")
        .WithSummary("Lưu theo lô câu trả lời đã chọn")
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status410Gone)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // ===== Nộp bài (idempotent) =====
        app.MapPost("/api/public/attempts/{id:guid}/submit", async (Guid id, AppDbContext db, AttemptsService svc, CancellationToken ct) =>
        {
            try
            {
                var dto = await svc.SubmitAsync(id, ct);
                if (dto is null)
                    return ApiErrors.NotFound("Không tìm thấy lượt làm bài");
                return Results.Ok(dto);
            }
            catch (AttemptConflictException ex)
            {
                return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, ex.Message);
            }
            catch (AttemptGoneException ex)
            {
                return ApiErrors.Problem(HttpStatusCode.Gone, "attempt.gone", ex.Message);
            }
        })
        .WithName("public.attempt.submit")
        .WithSummary("Nộp bài → chấm ở server, trả kết quả theo show_answers")
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status410Gone);

        return app;
    }

    /// <summary>Cookie thiết bị (không định danh): lấy sẵn có hoặc tạo mới + set 1 năm.</summary>
    internal static string GetOrCreateDevice(HttpContext ctx)
    {
        var device = ctx.Request.Cookies[DeviceCookie];
        if (!string.IsNullOrWhiteSpace(device))
            return device;

        device = GuidV7.New().ToString("n");
        ctx.Response.Cookies.Append(DeviceCookie, device, new CookieOptions
        {
            HttpOnly = true,
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Path = "/",
        });
        return device;
    }

    /// <summary>§12: IP lưu dạng SHA-256(ip + salt), không lưu IP thô.</summary>
    internal static string HashIp(HttpContext ctx, IConfiguration config)
    {
        var salt = config["Security:IpHashSalt"] ?? string.Empty;
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip + salt))).ToLowerInvariant();
    }
}
