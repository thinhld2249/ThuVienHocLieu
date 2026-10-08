using System.Security.Claims;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Notifications;

/// <summary>§10: thông báo in-app của người dùng hiện tại.</summary>
public static class NotificationsEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/me/notifications").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var uid = long.Parse(user.FindFirstValue(CurrentUserService.ClaimUid)!);
            var items = await db.Notifications.AsNoTracking()
                .Where(n => n.UserId == uid)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Link, n.CreatedAt, n.ReadAt != null))
                .ToListAsync(ct);
            return Results.Ok(new { items, unreadCount = items.Count(i => !i.Read) });
        })
        .WithName("me.notifications")
        .WithSummary("Thông báo của tôi (50 mới nhất)");

        g.MapPost("/read", async (ClaimsPrincipal user, AppDbContext db, MarkNotificationsReadRequest? req, CancellationToken ct) =>
        {
            var uid = long.Parse(user.FindFirstValue(CurrentUserService.ClaimUid)!);
            var now = DateTimeOffset.UtcNow;
            var q = req?.Ids is { Count: > 0 }
                ? db.Notifications.Where(n => n.UserId == uid && req.Ids.Contains(n.Id))
                : db.Notifications.Where(n => n.UserId == uid);
            var rows = await q.Where(n => n.ReadAt == null).ToListAsync(ct);
            foreach (var n in rows)
            {
                n.ReadAt = now;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = rows.Count });
        })
        .WithName("me.notifications.read")
        .WithSummary("Đánh dấu đã đọc (không ids = tất cả)");
    }
}

public record NotificationDto(long Id, string Type, string Title, string? Link, DateTimeOffset CreatedAt, bool Read);
public record MarkNotificationsReadRequest(List<long>? Ids);
