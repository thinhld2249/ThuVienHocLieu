using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Announcements;

public record PublicAnnouncementDto(long Id, string Title, string BodyHtml, bool IsPinned, string CreatedAt);

public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/announcements", async (HttpContext ctx, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var now = time.GetUtcNow();

            // Công khai + (GV thấy thêm thông báo "Giáo viên"); thông báo tổ xử lý ở feature Teams (M2)
            var items = await db.Announcements.AsNoTracking()
                .Where(a => (a.Audience == AnnouncementAudience.Public
                             || (a.Audience == AnnouncementAudience.Teachers && viewer.UserId != null))
                            && (a.PublishAt == null || a.PublishAt <= now)
                            && (a.ExpireAt == null || now < a.ExpireAt))
                .OrderByDescending(a => a.IsPinned)
                .ThenByDescending(a => a.CreatedAt)
                .Take(20)
                .Select(a => new PublicAnnouncementDto(a.Id, a.Title, a.BodyHtml, a.IsPinned, a.CreatedAt.ToString("o")))
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithName("public.announcements")
        .WithSummary("Thông báo theo vai trò người xem");
        return app;
    }
}
