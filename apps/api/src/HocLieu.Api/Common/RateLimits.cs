using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HocLieu.Common;

/// <summary>§8.4: rate limit built-in — mỗi policy = phân vùng + fixed window.</summary>
public static class RateLimits
{
    public const string GoogleAuth = "google-auth";      // 10/phút/IP
    public const string CreateAttempt = "create-attempt"; // 20/phút/IP
    public const string SaveAnswers = "save-answers";     // 120/phút/attempt
    public const string Report = "report";               // 5/giờ/IP
    public const string Import = "import";               // 10/phút/user

    public static void AddPolicies(IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "application/problem+json; charset=utf-8";
                await ctx.HttpContext.Response.WriteAsync(
                    """{"type":"https://hoclieu.dev/errors/rate_limited","title":"Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.","status":429,"code":"rate_limited"}""", ct);
            };

            // Phân vùng theo IP remote (IpAddress.ToString() không kèm port)
            o.AddPolicy(GoogleAuth, ctx => FixedWindow(RemoteIp(ctx),
                x => { x.PermitLimit = 10; x.Window = TimeSpan.FromMinutes(1); }));
            o.AddPolicy(CreateAttempt, ctx => FixedWindow(RemoteIp(ctx),
                x => { x.PermitLimit = 20; x.Window = TimeSpan.FromMinutes(1); }));
            o.AddPolicy(Report, ctx => FixedWindow(RemoteIp(ctx),
                x => { x.PermitLimit = 5; x.Window = TimeSpan.FromHours(1); }));

            // Phân vùng theo attemptId trong route
            o.AddPolicy(SaveAnswers, ctx => FixedWindow(ctx.Request.RouteValues["id"]?.ToString() ?? "none",
                x => { x.PermitLimit = 120; x.Window = TimeSpan.FromMinutes(1); }));

            // Phân vùng theo user (claim uid)
            o.AddPolicy(Import, ctx => FixedWindow(ctx.User.FindFirst("uid")?.Value ?? "unknown",
                x => { x.PermitLimit = 10; x.Window = TimeSpan.FromMinutes(1); }));
        });
    }

    private static string RemoteIp(HttpContext ctx)
        => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> FixedWindow(string key, Action<FixedWindowRateLimiterOptions> configure)
        => RateLimitPartition.GetFixedWindowLimiter(key, _ =>
        {
            var options = new FixedWindowRateLimiterOptions { QueueLimit = 0 };
            configure(options);
            return options;
        });
}
