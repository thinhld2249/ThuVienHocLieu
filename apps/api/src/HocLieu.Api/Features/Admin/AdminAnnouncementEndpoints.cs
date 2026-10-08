using System.Security.Claims;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/thong-bao + trang tĩnh (M6).</summary>
public static class AdminAnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAdminAnnouncementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/announcements", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Announcements.AsNoTracking()
                .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.CreatedAt)
                .Select(a => new AnnouncementRow(a.Id, a.Title, a.BodyHtml, a.Audience.ToString(),
                    a.TeamId, a.Team == null ? null : a.Team.Name, a.IsPinned, a.PublishAt, a.ExpireAt, a.CreatedAt))
                .ToListAsync(ct);
            var items = rows
                .Select(r => new AdminAnnouncementDto(r.Id, r.Title, r.BodyHtml, r.Audience,
                    r.TeamId, r.TeamName, r.IsPinned,
                    r.PublishAt?.ToString("o"), r.ExpireAt?.ToString("o"), r.CreatedAt.ToString("o")))
                .ToList();
            return Results.Ok(items);
        })
        .WithName("admin.announcements.list")
        .WithSummary("Mọi thông báo (công khai / giáo viên / tổ)")
        .RequireAuthorization("Admin");

        app.MapPost("/api/admin/announcements", async (
                AnnouncementRequest req, AppDbContext db, HttpContext ctx, IAuditLogger audit, CancellationToken ct) =>
            {
                var error = Validate(req);
                if (error is not null)
                    return ApiErrors.Validation(new() { ["body"] = [error] });
                var announcement = new Announcement
                {
                    Title = (req.Title ?? "").Trim(),
                    BodyHtml = HtmlSanitize.Clean(req.BodyHtml),
                    Audience = (AnnouncementAudience)Enum.Parse(typeof(AnnouncementAudience), req.Audience!, true),
                    TeamId = req.TeamId,
                    IsPinned = req.IsPinned ?? false,
                    PublishAt = ParseInstant(req.PublishAt),
                    ExpireAt = ParseInstant(req.ExpireAt),
                    CreatedBy = ActorId(ctx) ?? 0, // endpoint luôn có phiên Admin (RequireAuthorization)
                };
                db.Announcements.Add(announcement);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.announcement.create", "announcement", announcement.Id.ToString(),
                    new { title = announcement.Title, audience = announcement.Audience.ToString() }, ct);
                return Results.Created($"/api/admin/announcements/{announcement.Id}",
                    new AdminAnnouncementDto(announcement.Id, announcement.Title, announcement.BodyHtml,
                        announcement.Audience.ToString(), announcement.TeamId, null, announcement.IsPinned,
                        announcement.PublishAt?.ToString("o"), announcement.ExpireAt?.ToString("o"),
                        announcement.CreatedAt.ToString("o")));
            })
            .WithName("admin.announcements.create")
            .WithSummary("Tạo thông báo")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/announcements/{id:long}", async (
                long id, AnnouncementRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var error = Validate(req);
                if (error is not null)
                    return ApiErrors.Validation(new() { ["body"] = [error] });
                var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);
                if (announcement is null)
                    return ApiErrors.NotFound("Không tìm thấy thông báo.");
                announcement.Title = (req.Title ?? "").Trim();
                announcement.BodyHtml = HtmlSanitize.Clean(req.BodyHtml);
                announcement.Audience = (AnnouncementAudience)Enum.Parse(typeof(AnnouncementAudience), req.Audience!, true);
                announcement.TeamId = req.TeamId;
                announcement.IsPinned = req.IsPinned ?? false;
                announcement.PublishAt = ParseInstant(req.PublishAt);
                announcement.ExpireAt = ParseInstant(req.ExpireAt);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.announcement.update", "announcement", announcement.Id.ToString(), null, ct);
                return Results.Ok(new AdminAnnouncementDto(announcement.Id, announcement.Title, announcement.BodyHtml,
                    announcement.Audience.ToString(), announcement.TeamId, announcement.Team?.Name,
                    announcement.IsPinned,
                    announcement.PublishAt?.ToString("o"), announcement.ExpireAt?.ToString("o"),
                    announcement.CreatedAt.ToString("o")));
            })
            .WithName("admin.announcements.update")
            .WithSummary("Sửa thông báo")
            .RequireAuthorization("Admin");

        app.MapDelete("/api/admin/announcements/{id:long}", async (
                long id, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);
                if (announcement is null)
                    return ApiErrors.NotFound("Không tìm thấy thông báo.");
                db.Announcements.Remove(announcement);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.announcement.delete", "announcement", id.ToString(), null, ct);
                return Results.NoContent();
            })
            .WithName("admin.announcements.delete")
            .WithSummary("Xóa thông báo")
            .RequireAuthorization("Admin");

        // ===== Trang tĩnh =====
        app.MapGet("/api/admin/pages", async (AppDbContext db, CancellationToken ct) =>
            {
                var rows = await db.StaticPages.AsNoTracking()
                    .OrderBy(p => p.Slug)
                    .Select(p => new PageRow(p.Slug, p.Title, p.BodyMarkdown, p.UpdatedAt))
                    .ToListAsync(ct);
                return Results.Ok(rows
                    .Select(p => new AdminPageDto(p.Slug, p.Title, p.BodyMarkdown,
                        p.UpdatedAt is DateTimeOffset v ? v.ToString("o") : null))
                    .ToList());
            })
            .WithName("admin.pages.list")
            .WithSummary("Mọi trang tĩnh (Giới thiệu, Hướng dẫn, Chính sách dữ liệu…)")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/pages/{slug}", async (
                string slug, PageRequest req, AppDbContext db, HttpContext ctx, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Title))
                    return ApiErrors.Validation(new() { ["title"] = ["Thiếu tiêu đề trang."] });
                if (slug.Length is 0 or > 80)
                    return ApiErrors.Validation(new() { ["slug"] = ["Slug không hợp lệ (tối đa 80 ký tự)."] });

                var page = await db.StaticPages.FirstOrDefaultAsync(p => p.Slug == slug, ct);
                if (page is null)
                {
                    page = new StaticPage { Slug = slug, Title = req.Title.Trim(), BodyMarkdown = req.BodyMarkdown ?? "" };
                    db.StaticPages.Add(page);
                }
                else
                {
                    page.Title = req.Title.Trim();
                    page.BodyMarkdown = req.BodyMarkdown ?? "";
                }
                page.UpdatedBy = ActorId(ctx); // UpdatedAt do AppDbContext.SaveChanges gán tự động
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.page.save", "static_page", slug, new { title = page.Title }, ct);
                return Results.Ok(new AdminPageDto(page.Slug, page.Title, page.BodyMarkdown, page.UpdatedAt?.ToString("o")));
            })
            .WithName("admin.pages.upsert")
            .WithSummary("Tạo/sửa trang tĩnh (Markdown)")
            .RequireAuthorization("Admin");

        app.MapDelete("/api/admin/pages/{slug}", async (
                string slug, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var page = await db.StaticPages.FirstOrDefaultAsync(p => p.Slug == slug, ct);
                if (page is null)
                    return ApiErrors.NotFound("Không tìm thấy trang.");
                db.StaticPages.Remove(page);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.page.delete", "static_page", slug, null, ct);
                return Results.NoContent();
            })
            .WithName("admin.pages.delete")
            .WithSummary("Xóa trang tĩnh")
            .RequireAuthorization("Admin");

        return app;
    }

    /// <summary>UID người dùng hiện tại từ claim "uid" (luôn có với RequireAuthorization("Admin")).</summary>
    private static long? ActorId(HttpContext ctx)
        => ctx.User.FindFirst(CurrentUserService.ClaimUid)?.Value is { Length: > 0 } s && long.TryParse(s, out var v)
            ? v
            : null;

    private static string? Validate(AnnouncementRequest? req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Title))
            return "Thiếu tiêu đề thông báo.";
        if (!Enum.TryParse<AnnouncementAudience>(req.Audience, true, out var audience))
            return "audience phải là Public, Teachers hoặc Team.";
        if (audience == AnnouncementAudience.Team && req.TeamId is null or <= 0)
            return "Thông báo cho tổ bắt buộc chọn teamId.";
        if (req.PublishAt is { Length: > 0 } && ParseInstant(req.PublishAt) is null)
            return "publishAt không hợp lệ (ISO 8601).";
        if (req.ExpireAt is { Length: > 0 } && ParseInstant(req.ExpireAt) is null)
            return "expireAt không hợp lệ (ISO 8601).";
        return null;
    }

    private static DateTimeOffset? ParseInstant(string? s)
        => s is { Length: > 0 } && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var v) ? v : null;
}

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record AnnouncementRow(
    long Id, string Title, string BodyHtml, string Audience,
    long? TeamId, string? TeamName, bool IsPinned,
    DateTimeOffset? PublishAt, DateTimeOffset? ExpireAt, DateTimeOffset CreatedAt);

public record AdminAnnouncementDto(
    long Id, string Title, string BodyHtml, string Audience,
    long? TeamId, string? TeamName, bool IsPinned,
    string? PublishAt, string? ExpireAt, string CreatedAt);

public record AnnouncementRequest(string? Title, string? BodyHtml, string? Audience, long? TeamId,
    bool? IsPinned, string? PublishAt, string? ExpireAt);

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record PageRow(string Slug, string Title, string BodyMarkdown, DateTimeOffset? UpdatedAt);

public record AdminPageDto(string Slug, string Title, string BodyMarkdown, string? UpdatedAt);
public record PageRequest(string? Title, string? BodyMarkdown);
