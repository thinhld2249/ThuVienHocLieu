using System.Text.Json;
using HocLieu.Common;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HocLieu.Infrastructure.Data.Seed;

/// <summary>
/// Seed idempotent (chạy sau Migrate, spec §9): 8 chuyên mục, khối 1–5, 13 môn,
/// năm học 2026-2027 (hiện tại), settings mặc định, 3 trang tĩnh rỗng.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await SeedGradesAsync(db, ct);
        await SeedSubjectsAsync(db, ct);
        await SeedSectionsAsync(db, ct);
        await SeedSchoolYearsAsync(db, ct);
        await SeedSettingsAsync(db, ct);
        await SeedStaticPagesAsync(db, ct);
    }

    private static async Task SeedGradesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = (await db.Grades.Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        for (short i = 1; i <= 5; i++)
        {
            if (!existing.Contains(i))
                db.Grades.Add(new Grade { Id = i, Name = $"Khối {i}", Sort = i });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSubjectsAsync(AppDbContext db, CancellationToken ct)
    {
        var names = new (string Name, string Slug)[]
        {
            ("Tiếng Việt", "tieng-viet"),
            ("Toán", "toan"),
            ("Tiếng Anh", "tieng-anh"),
            ("Đạo đức", "dao-duc"),
            ("Tự nhiên và Xã hội", "tu-nhien-va-xa-hoi"),
            ("Khoa học", "khoa-hoc"),
            ("Lịch sử và Địa lí", "lich-su-va-dia-li"),
            ("Tin học", "tin-hoc"),
            ("Công nghệ", "cong-nghe"),
            ("Âm nhạc", "am-nhac"),
            ("Mĩ thuật", "mi-thuat"),
            ("Giáo dục thể chất", "giao-duc-the-chat"),
            ("Hoạt động trải nghiệm", "hoat-dong-trai-nghiem"),
        };
        var existing = (await db.Subjects.Select(s => s.Slug).ToListAsync(ct)).ToHashSet();
        var sort = 1;
        foreach (var (name, slug) in names)
        {
            if (existing.Contains(slug))
            {
                sort++;
                continue;
            }
            db.Subjects.Add(new Subject { Name = name, Slug = slug, Sort = (short)sort });
            sort++;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSectionsAsync(AppDbContext db, CancellationToken ct)
    {
        // 2026-10-09: "Bài giảng điện tử" tạm ẩn (is_active = false) — chưa dùng,
        // video chuyển sang link nhúng trong mô tả (decisions.md). Bật lại tại /admin/danh-muc.
        var sections = new (string Slug, string Name, string Icon, string Color, SectionContentKind Kind, PublishMode DefaultPublish, ContentScope DefaultScope, bool RequireWeek, bool IsInternal, bool IsActive)[]
        {
            ("bai-giang-dien-tu", "Bài giảng điện tử", "presentation", "#2f6fdb", SectionContentKind.Document, PublishMode.Visible, ContentScope.Public, false, false, false),
            ("phan-phoi-chuong-trinh", "Phân phối chương trình", "calendar-range", "#0e8a87", SectionContentKind.Document, PublishMode.Visible, ContentScope.Public, false, false, true),
            ("ke-hoach-bai-day", "Kế hoạch bài dạy", "book-open", "#3a8d3f", SectionContentKind.Document, PublishMode.Visible, ContentScope.Public, false, false, true),
            ("bai-tap-cuoi-tuan", "Bài tập cuối tuần", "pencil-line", "#e07a1f", SectionContentKind.Both, PublishMode.Hidden, ContentScope.Public, true, false, true),
            ("de-khao-sat", "Đề khảo sát", "file-search", "#c2375b", SectionContentKind.Both, PublishMode.Hidden, ContentScope.Public, false, false, true),
            ("chuyen-de", "Chuyên đề", "lightbulb", "#8a5a2b", SectionContentKind.Document, PublishMode.Visible, ContentScope.Public, false, false, true),
            ("ke-hoach-chu-nhiem", "Kế hoạch chủ nhiệm", "calendar-check", "#6b5bd6", SectionContentKind.Document, PublishMode.Visible, ContentScope.Public, false, false, true),
            ("ho-so-to", "Hồ sơ tổ", "users", "#5e6b7a", SectionContentKind.Document, PublishMode.Visible, ContentScope.Team, false, true, true),
        };

        var existing = (await db.Sections.Select(s => s.Slug).ToListAsync(ct)).ToHashSet();
        var sort = 1;
        foreach (var s in sections)
        {
            if (existing.Contains(s.Slug))
            {
                sort++;
                continue;
            }
            db.Sections.Add(new Section
            {
                Slug = s.Slug,
                Name = s.Name,
                Icon = s.Icon,
                Color = s.Color,
                Sort = (short)sort,
                ContentKind = s.Kind,
                DefaultPublishMode = s.DefaultPublish,
                DefaultScope = s.DefaultScope,
                RequireWeek = s.RequireWeek,
                IsInternal = s.IsInternal,
                IsActive = s.IsActive,
            });
            sort++;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSchoolYearsAsync(AppDbContext db, CancellationToken ct)
    {
        var name = "2026-2027";
        var exists = await db.SchoolYears.AnyAsync(y => y.Name == name, ct);
        if (!exists)
        {
            // Nếu chưa có năm nào hiện tại → 2026-2027 là hiện tại (spec §9 seed)
            var hasCurrent = await db.SchoolYears.AnyAsync(y => y.IsCurrent, ct);
            db.SchoolYears.Add(new SchoolYear
            {
                Name = name,
                StartDate = new DateOnly(2026, 9, 5),
                EndDate = new DateOnly(2027, 5, 31),
                IsCurrent = !hasCurrent,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSettingsAsync(AppDbContext db, CancellationToken ct)
    {
        var defaults = new Dictionary<string, string>
        {
            ["site.name"] = "\"Học Liệu\"",
            ["site.logo_file_id"] = "null",
            ["site.contact"] = "{}",
            ["auth.auto_approve_domains"] = "[]",
            ["content.require_review"] = "false",
            ["content.show_author_public"] = "true",
            ["download.guest_default"] = "false",
            ["upload.max_mb"] = "50",
            // 2026-10-09: bỏ mp4 — video dùng link nhúng trong mô tả (decisions.md)
            ["upload.allowed_ext"] = "[\"pdf\",\"doc\",\"docx\",\"ppt\",\"pptx\",\"xls\",\"xlsx\",\"jpg\",\"jpeg\",\"png\",\"webp\"]",
        };
        var existing = (await db.AppSettings.Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        foreach (var (key, value) in defaults)
        {
            if (existing.Contains(key))
                continue;
            // kiểm tra json hợp lệ trước khi ghi
            _ = JsonSerializer.Deserialize<JsonElement>(value);
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedStaticPagesAsync(AppDbContext db, CancellationToken ct)
    {
        var pages = new (string Slug, string Title)[]
        {
            ("gioi-thieu", "Giới thiệu"),
            ("huong-dan", "Hướng dẫn"),
            ("chinh-sach-du-lieu", "Chính sách dữ liệu"),
        };
        var existing = (await db.StaticPages.Select(p => p.Slug).ToListAsync(ct)).ToHashSet();
        foreach (var (slug, title) in pages)
        {
            if (existing.Contains(slug))
                continue;
            db.StaticPages.Add(new StaticPage { Slug = slug, Title = title, BodyMarkdown = "" });
        }
        await db.SaveChangesAsync(ct);
    }
}
