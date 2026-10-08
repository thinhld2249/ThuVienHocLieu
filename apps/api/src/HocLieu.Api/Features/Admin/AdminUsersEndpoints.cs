using System.Security.Claims;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>
/// §5.4 /admin/nguoi-dung (M6): bảng người dùng + duyệt/khóa/cấp quyền/gán tổ,
/// chuyển quyền sở hữu nội dung, đăng xuất mọi thiết bị.
/// </summary>
public static class AdminUsersEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/users", async (
                AppDbContext db,
                [FromQuery] string? status, [FromQuery] string? systemRole,
                [FromQuery] long? teamId, [FromQuery] string? q,
                [FromQuery] int? page, [FromQuery] int? pageSize,
                CancellationToken ct) =>
            {
                IQueryable<User> query = db.Users.AsNoTracking();
                if (Enum.TryParse<Domain.UserStatus>(status, true, out var st))
                    query = query.Where(u => u.Status == st);
                if (Enum.TryParse<Domain.SystemRole>(systemRole, true, out var sr))
                    query = query.Where(u => u.SystemRole == sr);
                if (teamId is not null)
                    query = query.Where(u => u.TeamMemberships.Any(m => m.TeamId == teamId));
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var text = q.Trim();
                    query = query.Where(u => u.FullName.Contains(text) || u.Email.Contains(text));
                }

                (var p, var ps) = Pagination.Parse(page, pageSize, 25);
                var total = await query.CountAsync(ct);
                var rows = await query
                    .OrderBy(u => u.FullName)
                    .Skip((p - 1) * ps).Take(ps)
                    .Select(u => new AdminUserRow(
                        u.Id, u.FullName, u.Email, u.Phone, u.AvatarUrl,
                        u.Status.ToString(), u.StatusReason, u.SystemRole.ToString(),
                        u.LastLoginAt, u.CreatedAt,
                        u.RequestedTeamId, u.RequestedTeam == null ? null : u.RequestedTeam.Name,
                        u.TeamMemberships.Select(m => new AdminUserTeamDto(m.TeamId, m.Team.Name, m.Role.ToString())).ToList()))
                    .ToListAsync(ct);
                var items = rows
                    .Select(r => new AdminUserDetailDto(r.Id, r.FullName, r.Email, r.Phone, r.AvatarUrl,
                        r.Status, r.StatusReason, r.SystemRole,
                        r.LastLoginAt?.ToString("o"), r.CreatedAt.ToString("o"),
                        r.RequestedTeamId, r.RequestedTeamName, r.Teams))
                    .ToList();
                return Results.Ok(new PagedResult<AdminUserDetailDto>(items, total, p, ps));
            })
            .WithName("admin.users.list")
            .WithSummary("Bảng người dùng (lọc trạng thái/vai trò/tổ, tìm kiếm)")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("status"))
            .WithMetadata(new QueryParameter("systemRole"))
            .WithMetadata(new QueryParameter("teamId", "integer"))
            .WithMetadata(new QueryParameter("q"))
            .WithMetadata(new QueryParameter("page", "integer"))
            .WithMetadata(new QueryParameter("pageSize", "integer"));

        app.MapPatch("/api/admin/users/{id:long}", async (
                long id, UserPatchRequest req, AdminService svc, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null)
                    return ApiErrors.Validation(new() { ["body"] = ["Thiếu dữ liệu."] });
                try
                {
                    var user = await svc.ApplyUserPatchAsync(id, req.Status, req.StatusReason, req.SystemRole, req.TeamIds, ct);
                    await audit.LogAsync("admin.user.update", "user", user.Id.ToString(), new
                    {
                        status = user.Status.ToString(),
                        systemRole = user.SystemRole.ToString(),
                        statusReason = user.StatusReason,
                        teamIds = user.TeamMemberships.Select(m => m.TeamId).ToList(),
                    }, ct);
                    return Results.Ok(await ToDetailDtoAsync(db, id, ct));
                }
                catch (AdminFlowException ex)
                {
                    return AdminFlowResult(ex);
                }
            })
            .WithName("admin.users.patch")
            .WithSummary("Duyệt/từ chối, khóa/mở khóa, cấp/thu quyền Admin, gán tổ")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/users/{id:long}/transfer-content", async (
                long id, TransferContentRequest req, AdminService svc, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req?.ToUserId is null or <= 0)
                    return ApiErrors.Validation(new() { ["toUserId"] = ["Thiếu người nhận nội dung."] });
                try
                {
                    var (documents, quizzes) = await svc.TransferContentAsync(id, req.ToUserId!.Value, ct);
                    await audit.LogAsync("admin.user.transfer_content", "user", id.ToString(),
                        new { toUserId = req.ToUserId, documents, quizzes }, ct);
                    return Results.Ok(new { documents, quizzes });
                }
                catch (AdminFlowException ex)
                {
                    return AdminFlowResult(ex);
                }
            })
            .WithName("admin.users.transfer_content")
            .WithSummary("Chuyển toàn bộ nội dung sang GV khác (GV nghỉ/chuyển trường)")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/users/{id:long}/logout-all", async (
                long id, AppDbContext db, SessionStampService stamps, IAuditLogger audit, CancellationToken ct) =>
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
                if (user is null)
                    return ApiErrors.NotFound("Không tìm thấy người dùng.");
                user.SecurityStamp = Guid.NewGuid();
                await db.SaveChangesAsync(ct);
                await stamps.InvalidateUserAsync(id, ct);
                await audit.LogAsync("admin.user.logout_all", "user", id.ToString(), null, ct);
                return Results.NoContent();
            })
            .WithName("admin.users.logout_all")
            .WithSummary("Đăng xuất mọi thiết bị của người dùng")
            .RequireAuthorization("Admin");

        return app;
    }

    /// <summary>Hàng thô từ DB — format thời gian làm trong bộ nhớ (không phụ thuộc dịch chuyển EF).</summary>
    private sealed record AdminUserRow(
        long Id, string FullName, string Email, string? Phone, string? AvatarUrl,
        string Status, string? StatusReason, string SystemRole,
        DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt,
        long? RequestedTeamId, string? RequestedTeamName,
        IReadOnlyList<AdminUserTeamDto> Teams);

    private static async Task<AdminUserDetailDto?> ToDetailDtoAsync(AppDbContext db, long id, CancellationToken ct)
    {
        var r = await db.Users.AsNoTracking()
            .Include(u => u.TeamMemberships).ThenInclude(m => m.Team)
            .Include(u => u.RequestedTeam)
            .Where(u => u.Id == id)
            .Select(u => new AdminUserRow(
                u.Id, u.FullName, u.Email, u.Phone, u.AvatarUrl,
                u.Status.ToString(), u.StatusReason, u.SystemRole.ToString(),
                u.LastLoginAt, u.CreatedAt,
                u.RequestedTeamId, u.RequestedTeam == null ? null : u.RequestedTeam.Name,
                u.TeamMemberships.Select(m => new AdminUserTeamDto(m.TeamId, m.Team.Name, m.Role.ToString())).ToList()))
            .FirstOrDefaultAsync(ct);
        if (r is null)
            return null;
        return new AdminUserDetailDto(r.Id, r.FullName, r.Email, r.Phone, r.AvatarUrl,
            r.Status, r.StatusReason, r.SystemRole,
            r.LastLoginAt?.ToString("o"), r.CreatedAt.ToString("o"),
            r.RequestedTeamId, r.RequestedTeamName, r.Teams);
    }

    internal static IResult AdminFlowResult(AdminFlowException ex) =>
        Results.Problem(
            detail: null, title: ex.Title, statusCode: ex.Status,
            type: $"https://hoclieu.dev/errors/{ex.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
}

public record AdminUserTeamDto(long TeamId, string TeamName, string Role);

public record AdminUserDetailDto(
    long Id, string FullName, string Email, string? Phone, string? AvatarUrl,
    string Status, string? StatusReason, string SystemRole,
    string? LastLoginAt, string CreatedAt,
    long? RequestedTeamId, string? RequestedTeamName,
    IReadOnlyList<AdminUserTeamDto> Teams);

public record UserPatchRequest(string? Status, string? StatusReason, string? SystemRole, long[]? TeamIds);
public record TransferContentRequest(long? ToUserId);
