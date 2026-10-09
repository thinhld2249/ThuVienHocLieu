using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HocLieu.Features.Home;

public record HomeItemDto(string Kind, long Id, string Slug, string Title, string? Summary, string? SectionSlug, string? SectionName, short? Grade, string? SubjectName, string CreatedAt);
public record HomeOpenQuizDto(long Id, string Slug, string Title, string? SubjectName, short? Grade, string? WeekLabel, string? UntilAt);
public record HomeStatsDto(int Documents, int Quizzes, int Teachers);
public record HomeDto(
    IReadOnlyList<HomeOpenQuizDto> OpenQuizzes,
    IReadOnlyList<HomeItemDto> Recent,
    IReadOnlyList<HomeItemDto> Featured,
    IReadOnlyList<Taxonomy.SectionDto> Sections,
    IReadOnlyList<Announcements.PublicAnnouncementDto> Announcements,
    HomeStatsDto Stats);

/// <summary>Hàng thô từ DB — format chuỗi (ISO, nhãn tuần) làm ở trong bộ nhớ để không phụ thuộc dịch chuyển EF.</summary>
public static class HomeEndpoints
{
    private sealed record OpenQuizRow(long Id, string Slug, string Title, string? SubjectName, short? Grade, short? WeekNo, DateTimeOffset? UntilAt);
    private sealed record ItemRow(string Kind, long Id, string Slug, string Title, string? Summary, string? SectionSlug, string? SectionName, short? Grade, string? SubjectName, DateTimeOffset CreatedAt);
    private sealed record AnnouncementRow(long Id, string Title, string BodyHtml, bool IsPinned, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapHomeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/home", async (short? grade, AppDbContext db, TimeProvider time, IMemoryCache cache, CancellationToken ct) =>
        {
            short? gradeId = grade is > 0 and < 6 ? grade.Value : null;

            // Cache công khai tối đa 30s (spec §4.4): trang chủ là endpoint nóng nhất
            // (đỉnh cao tối T6-CN); nội dung luôn là scope Public cho khách nên cache an toàn.
            var home = await cache.GetOrCreateAsync($"public:home:{gradeId?.ToString() ?? "all"}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                var now = time.GetUtcNow();

                // Bài tập đang mở của khối (scope Public, live, sắp tới đóng trước)
                var openQuizRows = await db.Quizzes.AsNoTracking()
                        .VisibleTo(GuestViewer.Instance, now)
                        .Where(q => q.Scope == Domain.ContentScope.Public && q.GradeId == gradeId)
                        .OrderBy(q => q.PublishUntil)
                        .Take(12)
                        .Select(q => new OpenQuizRow(
                            q.Id, q.Slug, q.Title,
                            q.Subject == null ? null : q.Subject.Name,
                            q.GradeId, q.WeekNo, q.PublishUntil))
                        .ToListAsync(ct);

                var openQuizzes = openQuizRows
                    .Select(r => new HomeOpenQuizDto(r.Id, r.Slug, r.Title, r.SubjectName, r.Grade,
                        r.WeekNo is null ? null : $"Tuần {r.WeekNo}",
                        r.UntilAt?.ToString("o")))
                    .ToList();

                // Mới cập nhật: tài liệu + bài tập public live, 12 mục
                var recentDocRows = await db.Documents.AsNoTracking()
                    .VisibleTo(GuestViewer.Instance, now)
                    .Where(d => d.Scope == Domain.ContentScope.Public)
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(12)
                    .Select(d => new ItemRow(
                        "document", d.Id, d.Slug, d.Title, d.Summary,
                        d.Section == null ? null : d.Section.Slug,
                        d.Section == null ? null : d.Section.Name,
                        d.GradeId,
                        d.Subject == null ? null : d.Subject.Name,
                        d.CreatedAt))
                    .ToListAsync(ct);

                var recentQuizRows = await db.Quizzes.AsNoTracking()
                    .VisibleTo(GuestViewer.Instance, now)
                    .Where(q => q.Scope == Domain.ContentScope.Public)
                    .OrderByDescending(q => q.CreatedAt)
                    .Take(12)
                    .Select(q => new ItemRow(
                        "quiz", q.Id, q.Slug, q.Title, null,
                        q.Section == null ? null : q.Section.Slug,
                        q.Section == null ? null : q.Section.Name,
                        q.GradeId,
                        q.Subject == null ? null : q.Subject.Name,
                        q.CreatedAt))
                    .ToListAsync(ct);

                var recent = recentDocRows.Concat(recentQuizRows)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(12)
                    .Select(ToItemDto)
                    .ToList();

                // Nổi bật (Admin ghim)
                var featuredRows = await db.Documents.AsNoTracking()
                    .VisibleTo(GuestViewer.Instance, now)
                    .Where(d => d.Scope == Domain.ContentScope.Public && d.IsFeatured)
                    .OrderByDescending(d => d.UpdatedAt)
                    .Take(4)
                    .Select(d => new ItemRow(
                        "document", d.Id, d.Slug, d.Title, d.Summary,
                        d.Section == null ? null : d.Section.Slug,
                        d.Section == null ? null : d.Section.Name,
                        d.GradeId,
                        d.Subject == null ? null : d.Subject.Name,
                        d.CreatedAt))
                    .ToListAsync(ct);

                var featured = featuredRows.Select(ToItemDto).ToList();

                var sections = await db.Sections.AsNoTracking()
                    .Where(s => s.IsActive && !s.IsInternal)
                    .OrderBy(s => s.Sort)
                    .Select(s => new Taxonomy.SectionDto(s.Id, s.Slug, s.Name, s.Icon, s.Color, s.Sort, s.ContentKind.ToString(), s.RequireWeek, s.IsInternal))
                    .ToListAsync(ct);

                var announcementRows = await db.Announcements.AsNoTracking()
                    .Where(a => a.Audience == Domain.AnnouncementAudience.Public
                                && (a.PublishAt == null || a.PublishAt <= now)
                                && (a.ExpireAt == null || now < a.ExpireAt))
                    .OrderByDescending(a => a.IsPinned)
                    .ThenByDescending(a => a.CreatedAt)
                    .Take(5)
                    .Select(a => new AnnouncementRow(a.Id, a.Title, a.BodyHtml, a.IsPinned, a.CreatedAt))
                    .ToListAsync(ct);

                var announcements = announcementRows
                    .Select(r => new Announcements.PublicAnnouncementDto(r.Id, r.Title, r.BodyHtml, r.IsPinned, r.CreatedAt.ToString("o")))
                    .ToList();

                //DbContext không cho phép thao tác song song → chạy lần lượt
                var documentCount = await db.Documents.CountAsync(d => !d.IsDeleted, ct);
                var quizCount = await db.Quizzes.CountAsync(q => !q.IsDeleted, ct);
                var teacherCount = await db.Users.CountAsync(u => u.Status == Domain.UserStatus.Active, ct);

                return new HomeDto(
                    openQuizzes,
                    recent,
                    featured,
                    sections,
                    announcements,
                    new HomeStatsDto(documentCount, quizCount, teacherCount));
            });
            return Results.Ok(home);
        })
        .WithName("public.home")
        .WithSummary("Trang chủ: bài tập đang mở, mới cập nhật, nổi bật, thống kê")
        .WithMetadata(new QueryParameter("grade", "integer"));

        // Sitemap XML — chỉ nội dung công khai đang hiện (spec §5.1).
        // Scheme cố định https: Caddy terminate TLS, api nhận http nội bộ.
        app.MapGet("/sitemap.xml", async (AppDbContext db, TimeProvider time, HttpContext ctx, CancellationToken ct) =>
            {
                var now = time.GetUtcNow();
                var baseUri = "https://" + ctx.Request.Host.Value;
                var urls = new List<(string Location, DateTimeOffset LastMod)> { ("", now) };

                var docRows = await db.Documents.AsNoTracking()
                    .VisibleTo(GuestViewer.Instance, now)
                    .Where(d => d.Scope == Domain.ContentScope.Public)
                    .Select(d => new { d.Slug, d.Id, d.UpdatedAt })
                    .ToListAsync(ct);
                urls.AddRange(docRows.Select(d => ($"/tai-lieu/{d.Slug}-{d.Id}", d.UpdatedAt)));

                var quizRows = await db.Quizzes.AsNoTracking()
                    .VisibleTo(GuestViewer.Instance, now)
                    .Where(q => q.Scope == Domain.ContentScope.Public)
                    .Select(q => new { q.Slug, q.Id, q.UpdatedAt })
                    .ToListAsync(ct);
                urls.AddRange(quizRows.Select(q => ($"/bai-tap/{q.Slug}-{q.Id}", q.UpdatedAt)));

                var sb = new System.Text.StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                foreach (var (loc, mod) in urls)
                {
                    sb.Append("<url><loc>").Append($"{baseUri}{loc}").Append("</loc>");
                    sb.Append("<lastmod>").Append(mod.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append("</lastmod></url>");
                }
                sb.Append("</urlset>");
                return Results.Text(sb.ToString(), "application/xml; charset=utf-8");
            })
            .WithName("sitemap")
            .WithSummary("Sitemap XML của nội dung công khai đang hiện");
        return app;
    }

    private static HomeItemDto ToItemDto(ItemRow r)
        => new(r.Kind, r.Id, r.Slug, r.Title, r.Summary, r.SectionSlug, r.SectionName, r.Grade, r.SubjectName, r.CreatedAt.ToString("o"));
}

public static class GuestViewer
{
    public static readonly ViewerContext Instance =
        new(null, false, new HashSet<long>(), new HashSet<long>(), new HashSet<long>());
}
