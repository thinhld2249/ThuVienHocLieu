using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Features.Teams;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 + §10: Admin quản lý tổ, bổ nhiệm tổ trưởng, lời mời toàn hệ thống.</summary>
public static class AdminTeamsEndpoints
{
    public static IEndpointRouteBuilder MapAdminTeamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/teams", async (TeamsService svc, CancellationToken ct) =>
            Results.Ok(await svc.AdminListTeamsAsync(ct)))
            .WithName("admin.teams.list")
            .WithSummary("Danh sách mọi tổ")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/teams", async (
                CreateTeamRequest req, TeamsService svc, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    var team = await svc.AdminCreateTeamAsync(req?.Name ?? "", req?.Description, req?.GradeId, ct);
                    return Results.Created($"/api/admin/teams/{team.Id}", team);
                }
                catch (TeamFlowException ex)
                {
                    return AdminTeamResult(ex);
                }
            })
            .WithName("admin.teams.create")
            .WithSummary("Tạo tổ")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/teams/{id:long}", async (
                long id, UpdateTeamRequest req, TeamsService svc, CancellationToken ct) =>
            {
                try
                {
                    if (req is null)
                        return ApiErrors.Validation(new() { ["name"] = ["Thiếu dữ liệu."] });
                    return Results.Ok(await svc.AdminUpdateTeamAsync(
                        id, req.Name, req.Description, req.GradeId, req.IsActive, ct));
                }
                catch (TeamFlowException ex)
                {
                    return AdminTeamResult(ex);
                }
            })
            .WithName("admin.teams.update")
            .WithSummary("Sửa tổ (tên, mô tả, khối, hoạt động/ngừng)")
            .RequireAuthorization("Admin");

        app.MapDelete("/api/admin/teams/{id:long}", async (
                long id, TeamsService svc, CancellationToken ct) =>
            {
                try
                {
                    await svc.AdminDeactivateTeamAsync(id, ct);
                    return Results.NoContent();
                }
                catch (TeamFlowException ex)
                {
                    return AdminTeamResult(ex);
                }
            })
            .WithName("admin.teams.deactivate")
            .WithSummary("Ngưng hoạt động tổ (không xóa cứng)")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/teams/{id:long}/lead", async (
                long id, SetLeadRequest req, TeamsService svc, CancellationToken ct) =>
            {
                try
                {
                    if (req is null || req.UserId <= 0)
                        return ApiErrors.Validation(new() { ["userId"] = ["Thiếu giáo viên được bổ nhiệm."] });
                    return Results.Ok(await svc.AdminSetLeadAsync(id, req.UserId, ct));
                }
                catch (TeamFlowException ex)
                {
                    return AdminTeamResult(ex);
                }
            })
            .WithName("admin.teams.lead")
            .WithSummary("Bổ nhiệm tổ trưởng (người cũ thành Member)")
            .RequireAuthorization("Admin");

        app.MapGet("/api/admin/invitations", async (
                TeamsService svc, [FromQuery] long? teamId, [FromQuery] string? status,
                HttpContext ctx, CancellationToken ct) =>
            Results.Ok(await svc.AdminListInvitationsAsync(teamId, status, BaseUrl(ctx), ct)))
            .WithName("admin.invitations.list")
            .WithSummary("Mọi lời mời (lọc theo tổ/trạng thái)")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("teamId", "integer"))
            .WithMetadata(new QueryParameter("status"));

        app.MapPost("/api/admin/invitations", async (
                AdminCreateInvitationsRequest req, TeamsService svc, HttpContext ctx, CancellationToken ct) =>
            {
                try
                {
                    if (req is null)
                        return ApiErrors.Validation(new() { ["emails"] = ["Thiếu dữ liệu."] });
                    var role = (req.TeamRole ?? "Member") switch
                    {
                        "Member" => TeamRole.Member,
                        "Deputy" => TeamRole.Deputy,
                        "Lead" => TeamRole.Lead,
                        _ => throw new TeamFlowException(422, "validation",
                            "teamRole phải là Member, Deputy hoặc Lead."),
                    };
                    return Results.Ok(await svc.CreateInvitationsAsync(
                        AdminViewer(), req.TeamId, req.Emails ?? [], role, req.Message, BaseUrl(ctx), true, ct));
                }
                catch (TeamFlowException ex)
                {
                    return AdminTeamResult(ex);
                }
            })
            .WithName("admin.invitations.create")
            .WithSummary("Mời vào bất kỳ tổ/vai trò nào (kể cả tổ trưởng)")
            .RequireAuthorization("Admin");

        return app;
    }

    /// <summary>Admin gọi service với ViewerContext Admin toàn cục.</summary>
    private static ViewerContext AdminViewer()
        => new(null, true, new HashSet<long>(), new HashSet<long>(), new HashSet<long>());

    internal static string? BaseUrl(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetService<IConfiguration>()?["Site:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }

    private static IResult AdminTeamResult(TeamFlowException ex) =>
        Results.Problem(
            detail: null, title: ex.Title, statusCode: ex.Status,
            type: $"https://hoclieu.dev/errors/{ex.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
}
