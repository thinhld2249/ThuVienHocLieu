using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HocLieu.Features.Teams;

/// <summary>§5.3 + §10: không gian tổ — thành viên, chờ duyệt, lời mời, thông báo, thống kê.</summary>
public static class TeamManagementEndpoints
{
    public static IEndpointRouteBuilder MapTeamManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/teams/{id:long}", async (
                long id, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.GetTeamDetailAsync(viewer, id, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.get")
            .WithSummary("Chi tiết tổ (thành viên hoặc Admin)")
            .RequireAuthorization("ActiveTeacher");

        app.MapGet("/api/teams/{id:long}/members", async (
                long id, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.GetMembersAsync(viewer, id, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.members")
            .WithSummary("Thành viên tổ")
            .RequireAuthorization("ActiveTeacher");

        app.MapGet("/api/teams/{id:long}/join-requests", async (
                long id, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.GetJoinRequestsAsync(viewer, id, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.join_requests")
            .WithSummary("Hàng chờ duyệt GV xin vào tổ (Lead | Deputy | Admin)")
            .RequireAuthorization("ActiveTeacher");

        app.MapPost("/api/teams/{id:long}/join-requests/{userId:long}/approve", async (
                long id, long userId, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    var results = await svc.ApproveJoinRequestsAsync(viewer, id, [userId], ct);
                    return Results.Ok(results[0]);
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.join_request.approve")
            .WithSummary("Duyệt GV xin vào tổ")
            .RequireAuthorization("ActiveTeacher");

        app.MapPost("/api/teams/{id:long}/join-requests/{userId:long}/reject", async (
                long id, long userId, RejectJoinRequestRequest req,
                TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.RejectJoinRequestAsync(viewer, id, userId, req?.Reason, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.join_request.reject")
            .WithSummary("Từ chối GV xin vào tổ (kèm lý do)")
            .RequireAuthorization("ActiveTeacher");

        // Duyệt hàng loạt — đặt TRƯỚC route {userId:long}/approve không cần vì constraint :long
        // đã tách "approve" (không phải số)
        app.MapPost("/api/teams/{id:long}/join-requests/approve", async (
                long id, BulkApproveRequest req,
                TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    if (req?.UserIds is not { Count: > 0 })
                        return ApiErrors.Validation(new() { ["userIds"] = ["Chọn ít nhất một yêu cầu."] });
                    return Results.Ok(await svc.ApproveJoinRequestsAsync(viewer, id, req.UserIds, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.join_requests.approve_bulk")
            .WithSummary("Duyệt hàng loạt")
            .RequireAuthorization("ActiveTeacher");

        app.MapPatch("/api/teams/{id:long}/members/{userId:long}", async (
                long id, long userId, SetMemberRoleRequest req,
                TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    if (string.IsNullOrWhiteSpace(req?.Role))
                        return ApiErrors.Validation(new() { ["role"] = ["Thiếu vai trò."] });
                    return Results.Ok(await svc.SetMemberRoleAsync(viewer, id, userId, req.Role.Trim(), ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.member.role")
            .WithSummary("Đặt/bỏ tổ phó (Lead | Admin)")
            .RequireAuthorization("ActiveTeacher");

        app.MapDelete("/api/teams/{id:long}/members/{userId:long}", async (
                long id, long userId, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    await svc.RemoveMemberAsync(viewer, id, userId, ct);
                    return Results.NoContent();
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.member.remove")
            .WithSummary("Gỡ thành viên khỏi tổ (Lead | Admin)")
            .RequireAuthorization("ActiveTeacher");

        app.MapGet("/api/teams/{id:long}/invitations", async (
                long id, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.GetInvitationsAsync(viewer, id, BaseUrl(ctx), ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.invitations")
            .WithSummary("Lời mời của tổ (Lead | Deputy | Admin)")
            .RequireAuthorization("ActiveTeacher");

        app.MapPost("/api/teams/{id:long}/invitations", async (
                long id, CreateInvitationsRequest req,
                TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    var emails = req?.Emails ?? [];
                    if (emails.Count == 0)
                        return ApiErrors.Validation(new() { ["emails"] = ["Nhập ít nhất một email."] });
                    return Results.Ok(await svc.CreateInvitationsAsync(
                        viewer, id, emails, TeamRole.Member, req?.Message, BaseUrl(ctx), false, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.invitations.create")
            .WithSummary("Mời nhiều email vào tổ (vai trò Member)")
            .RequireAuthorization("ActiveTeacher");

        app.MapPost("/api/teams/{id:long}/invitations/{invId:guid}/resend", async (
                long id, Guid invId, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.ResendInvitationAsync(viewer, id, invId, BaseUrl(ctx), ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.invitation.resend")
            .WithSummary("Gửi lại lời mời (token mới, gia hạn 7 ngày)")
            .RequireAuthorization("ActiveTeacher");

        app.MapDelete("/api/teams/{id:long}/invitations/{invId:guid}", async (
                long id, Guid invId, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    await svc.RevokeInvitationAsync(viewer, id, invId, ct);
                    return Results.NoContent();
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.invitation.revoke")
            .WithSummary("Thu hồi lời mời")
            .RequireAuthorization("ActiveTeacher");

        app.MapGet("/api/teams/{id:long}/announcements", async (
                long id, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.GetTeamAnnouncementsAsync(viewer, id, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.announcements")
            .WithSummary("Thông báo tổ (thành viên xem)")
            .RequireAuthorization("ActiveTeacher");

        app.MapPost("/api/teams/{id:long}/announcements", async (
                long id, PostAnnouncementRequest req,
                TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    if (req is null)
                        return ApiErrors.Validation(new() { ["title"] = ["Thiếu nội dung."] });
                    return Results.Created(
                        $"/api/teams/{id}/announcements",
                        await svc.PostTeamAnnouncementAsync(
                            viewer, id, req.Title ?? "", req.BodyHtml, req.IsPinned, req.ExpireAt, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.announcements.create")
            .WithSummary("Đăng thông báo tổ (Lead | Deputy | Admin)")
            .RequireAuthorization("ActiveTeacher");

        app.MapGet("/api/teams/{id:long}/stats", async (
                long id, TeamsService svc, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
                    return Results.Ok(await svc.GetTeamStatsAsync(viewer, id, ct));
                }
                catch (TeamFlowException ex)
                {
                    return TeamResult(ex);
                }
            })
            .WithName("team.stats")
            .WithSummary("Thống kê tổ")
            .RequireAuthorization("ActiveTeacher");

        return app;
    }

    /// <summary>Base URL cho link mời — Site:BaseUrl (env) hoặc origin của request hiện tại.</summary>
    internal static string? BaseUrl(HttpContext ctx)
    {
        var configured = ctx.RequestServices
            .GetService<IConfiguration>()?["Site:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }

    private static IResult TeamResult(TeamFlowException ex) =>
        Results.Problem(
            detail: null, title: ex.Title, statusCode: ex.Status,
            type: $"https://hoclieu.dev/errors/{ex.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
}
