using System.Text.Json;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HocLieu.Common;

/// <summary>
/// Đọc `app_settings` (key → jsonb) với cache bộ nhớ 60 giây (spec §5.4, §8.1).
/// Giá trị trong DB là JSON thô: `"50"`, `"[\"pdf\"]"`, `"true"`.
/// </summary>
public sealed class AppSettingsService(AppDbContext db, IMemoryCache cache, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
        => await GetAsync<int>(key, fallback, ct);

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default)
        => await GetAsync<bool>(key, fallback, ct);

    public async Task<string[]> GetStringArrayAsync(string key, string[] fallback, CancellationToken ct = default)
        => await GetAsync<string[]>(key, fallback, ct);

    /// <summary>Xóa cache của một key (gọi sau khi Admin cập nhật setting).</summary>
    public void Invalidate(string key) => cache.Remove($"setting:{key}");

    private async Task<T> GetAsync<T>(string key, T fallback, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (cache.TryGetValue($"setting:{key}", out object? cached)
            && cached is ValueTuple<DateTimeOffset, T> tuple
            && now - tuple.Item1 < TimeSpan.FromSeconds(60))
            return tuple.Item2;

        T value = fallback;
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is not null)
        {
            try
            {
                value = JsonSerializer.Deserialize<T>(row.Value, JsonOpts) ?? fallback;
            }
            catch (JsonException)
            {
                // jsonb hỏng → dùng fallback, không làm đổ request
            }
        }

        cache.Set($"setting:{key}", (now, value), TimeSpan.FromMinutes(5));
        return value;
    }
}

/// <summary>Mặc định theo spec §5.4 (seed DbSeeder giống nhau).</summary>
public static class SettingKeys
{
    public const string SiteName = "site.name";
    public const string SiteLogoFileId = "site.logo_file_id";
    public const string SiteContact = "site.contact";
    public const string AuthAutoApproveDomains = "auth.auto_approve_domains";
    public const string ContentRequireReview = "content.require_review";
    public const string ContentShowAuthorPublic = "content.show_author_public";
    public const string DownloadGuestDefault = "download.guest_default";
    public const string UploadMaxMb = "upload.max_mb";
    public const string UploadAllowedExt = "upload.allowed_ext";

    public const int DefaultMaxMb = 50;
    // mp4 đã bỏ — video dùng link nhúng trong mô tả (decisions.md 2026-10-09)
    public static readonly string[] DefaultAllowedExt =
        ["pdf", "doc", "docx", "ppt", "pptx", "xls", "xlsx", "jpg", "jpeg", "png", "webp"];
}
