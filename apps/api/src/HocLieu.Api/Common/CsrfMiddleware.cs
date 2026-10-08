using System.Text;
using System.Text.Json;

namespace HocLieu.Common;

/// <summary>
/// §3.4: cookie SameSite=Lax + mọi request ghi (POST/PUT/PATCH/DELETE) bắt buộc header
/// X-Requested-With: hoclieu. (Google login cũng gửi header này từ FE.)
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Requested-With";
    public const string ExpectedValue = "hoclieu";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsPut(ctx.Request.Method)
            || HttpMethods.IsPatch(ctx.Request.Method) || HttpMethods.IsDelete(ctx.Request.Method))
        {
            if (!ctx.Request.Headers.TryGetValue(HeaderName, out var values)
                || !string.Equals(values.ToString(), ExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/problem+json; charset=utf-8";
                var body = JsonSerializer.Serialize(new
                {
                    type = "https://hoclieu.dev/errors/csrf",
                    title = "Yêu cầu không hợp lệ. Hãy tải lại trang và thử lại.",
                    status = 403,
                    code = "csrf",
                });
                await ctx.Response.WriteAsync(body, Encoding.UTF8);
                return;
            }
        }
        await next(ctx);
    }
}
