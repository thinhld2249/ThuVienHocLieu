using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Common.Auth;

public static class CurrentUserService
{
    public const string ClaimUid = "uid";
    public const string ClaimStamp = "stamp";

    /// <summary>Dựng ViewerContext cho request hiện tại (spec §2.2, §4.3).</summary>
    private static ViewerContext Anonymous()
        => new(null, false, new HashSet<long>(), new HashSet<long>(), new HashSet<long>());

    public static async Task<ViewerContext> GetViewerAsync(HttpContext ctx, AppDbContext db, CancellationToken ct = default)
    {
        if (!ctx.User.Identity?.IsAuthenticated ?? true)
            return Anonymous();
        if (!long.TryParse(ctx.User.FindFirst(ClaimUid)?.Value, out var userId))
            return Anonymous();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        // Pending vẫn có phiên (xem /cho-duyet + khu công khai); Suspended/Rejected đã chặn ở OnValidatePrincipal
        if (user is null || user.Status is UserStatus.Suspended or UserStatus.Rejected)
            return Anonymous();

        var memberships = await db.TeamMembers.AsNoTracking()
            .Where(tm => tm.UserId == userId)
            .Select(tm => new { tm.TeamId, tm.Role })
            .ToListAsync(ct);

        return new ViewerContext(
            userId,
            user.SystemRole == SystemRole.Admin,
            memberships.Select(m => m.TeamId).ToHashSet(),
            memberships.Where(m => m.Role is TeamRole.Lead or TeamRole.Deputy).Select(m => m.TeamId).ToHashSet(),
            memberships.Where(m => m.Role == TeamRole.Lead).Select(m => m.TeamId).ToHashSet());
    }
}
