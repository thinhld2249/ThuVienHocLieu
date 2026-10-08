using System.Security.Claims;
using HocLieu.Common;
using HocLieu.Common.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HocLieu.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/google", async (
            GoogleLoginRequest req,
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            ClaimsPrincipal principal;
            try
            {
                principal = await auth.LoginAsync(req, ct);
            }
            catch (AuthFlowException ex)
            {
                return AuthFlowResult(ex);
            }
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            var uid = long.Parse(principal.FindFirst(CurrentUserService.ClaimUid)!.Value);
            var me = await auth.GetMeAsync(uid, ct);
            return Results.Ok(me);
        })
        .WithName("auth.google")
        .WithSummary("Đăng nhập bằng mã ID token của Google Identity Services")
        .RequireRateLimiting(RateLimits.GoogleAuth);

        app.MapPost("/api/auth/login", async (
            PasswordLoginRequest req,
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            ClaimsPrincipal principal;
            try
            {
                principal = await auth.LoginWithPasswordAsync(req, ct);
            }
            catch (AuthFlowException ex)
            {
                return AuthFlowResult(ex);
            }
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            var uid = long.Parse(principal.FindFirst(CurrentUserService.ClaimUid)!.Value);
            var me = await auth.GetMeAsync(uid, ct);
            return Results.Ok(me);
        })
        .WithName("auth.login")
        .WithSummary("Đăng nhập bằng email + mật khẩu")
        .RequireRateLimiting(RateLimits.GoogleAuth);

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
        .WithName("auth.logout")
        .WithSummary("Đăng xuất thiết bị hiện tại");

        app.MapPost("/api/auth/logout-all", async (
            AuthService auth,
            SessionStampService stamps,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!TryGetUid(ctx, out var uid))
                return Results.Unauthorized();
            await auth.LogoutAllAsync(uid, stamps, ct);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
        .WithName("auth.logout_all")
        .WithSummary("Đổi security_stamp — mọi thiết bị phải đăng nhập lại")
        .RequireAuthorization();

        app.MapGet("/api/me", async (AuthService auth, HttpContext ctx, CancellationToken ct) =>
        {
            if (!TryGetUid(ctx, out var uid))
                return Results.Unauthorized();
            var me = await auth.GetMeAsync(uid, ct);
            return me is null ? Results.Unauthorized() : Results.Ok(me);
        })
        .WithName("me.get")
        .WithSummary("Thông tin tài khoản đang đăng nhập")
        .RequireAuthorization();

        app.MapPut("/api/me", async (
            UpdateMeRequest req,
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!TryGetUid(ctx, out var uid))
                return Results.Unauthorized();
            try
            {
                var me = await auth.UpdateMeAsync(uid, req, ct);
                return me is null ? Results.Unauthorized() : Results.Ok(me);
            }
            catch (AuthFlowException ex)
            {
                return AuthFlowResult(ex);
            }
        })
        .WithName("me.update")
        .WithSummary("Cập nhật họ tên, SĐT; Pending chọn tổ chờ duyệt")
        .RequireAuthorization();

        app.MapPut("/api/me/password", async (
            SetPasswordRequest req,
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!TryGetUid(ctx, out var uid))
                return Results.Unauthorized();
            try
            {
                await auth.SetPasswordAsync(uid, req.NewPassword, ct);
                return Results.NoContent();
            }
            catch (AuthFlowException ex)
            {
                return AuthFlowResult(ex);
            }
        })
        .WithName("me.password")
        .WithSummary("Đặt/đổi mật khẩu đăng nhập của chính mình (chỉ Admin)")
        .RequireAuthorization("Admin");

        app.MapGet("/api/invitations/{token}", async (string token, AuthService auth, CancellationToken ct) =>
        {
            var info = await auth.GetInvitationAsync(token, ct);
            return info is null
                ? ApiErrors.NotFound("Không tìm thấy lời mời. Kiểm tra lại link hoặc liên hệ người mời.")
                : Results.Ok(info);
        })
        .WithName("invitations.info")
        .WithSummary("Thông tin lời mời (công khai, không lộ email đầy đủ)");

        return app;
    }

    private static bool TryGetUid(HttpContext ctx, out long userId)
    {
        userId = 0;
        return (ctx.User.Identity?.IsAuthenticated ?? false)
            && long.TryParse(ctx.User.FindFirst(CurrentUserService.ClaimUid)?.Value, out userId);
    }

    private static IResult AuthFlowResult(AuthFlowException ex) =>
        Results.Problem(
            detail: null, title: ex.Title, statusCode: ex.Status,
            type: $"https://hoclieu.dev/errors/{ex.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
}
