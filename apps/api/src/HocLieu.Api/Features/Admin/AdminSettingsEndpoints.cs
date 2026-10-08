using System.Security.Claims;
using System.Text.Json;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/cai-dat (M6): cài đặt hệ thống (key → jsonb, spec §9).</summary>
public static class AdminSettingsEndpoints
{
    /// <summary>9 key hợp lệ (spec §5.4) — key lạ bị từ chối.</summary>
    private static readonly string[] KnownKeys =
    [
        SettingKeys.SiteName,
        SettingKeys.SiteLogoFileId,
        SettingKeys.SiteContact,
        SettingKeys.AuthAutoApproveDomains,
        SettingKeys.ContentRequireReview,
        SettingKeys.ContentShowAuthorPublic,
        SettingKeys.DownloadGuestDefault,
        SettingKeys.UploadMaxMb,
        SettingKeys.UploadAllowedExt,
    ];

    public static IEndpointRouteBuilder MapAdminSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/settings", async (AppDbContext db, CancellationToken ct) =>
            {
                var rows = await db.AppSettings.AsNoTracking()
                    .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
                var settings = new Dictionary<string, JsonElement>();
                foreach (var key in KnownKeys)
                    settings[key] = JsonDocument.Parse(rows.TryGetValue(key, out var v) ? v : "{}").RootElement.Clone();
                return Results.Ok(new { settings });
            })
            .WithName("admin.settings.get")
            .WithSummary("Đọc mọi cài đặt hệ thống")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/settings", async (
                SettingsRequest req, AppDbContext db, AppSettingsService settingsSvc,
                HttpContext ctx, IAuditLogger audit, TimeProvider time, CancellationToken ct) =>
            {
                if (req?.Settings is null || req.Settings.Count == 0)
                    return ApiErrors.Validation(new() { ["settings"] = ["Thiếu cài đặt cần cập nhật."] });

                var unknown = req.Settings.Keys.Where(k => !KnownKeys.Contains(k)).ToList();
                if (unknown.Count > 0)
                    return ApiErrors.Validation(new() { ["settings"] = [$"Key không hợp lệ: {string.Join(", ", unknown)}."] });

                var typeError = ValidateTypes(req.Settings);
                if (typeError is not null)
                    return ApiErrors.Validation(new() { ["settings"] = [typeError] });

                var changed = new List<string>();
                foreach (var (key, value) in req.Settings)
                {
                    var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
                    var raw = value.GetRawText();
                    if (row is null)
                    {
                        row = new AppSetting { Key = key, Value = raw, UpdatedBy = ActorId(ctx), UpdatedAt = time.GetUtcNow() };
                        db.AppSettings.Add(row);
                    }
                    else
                    {
                        row.Value = raw;
                        row.UpdatedBy = ActorId(ctx);
                        row.UpdatedAt = time.GetUtcNow();
                    }
                    changed.Add(key);
                }

                await db.SaveChangesAsync(ct);
                foreach (var key in changed)
                    settingsSvc.Invalidate(key);
                await audit.LogAsync("admin.settings.update", "app_settings", null, new { keys = changed }, ct);
                return Results.Ok(new { updated = changed });
            })
            .WithName("admin.settings.put")
            .WithSummary("Cập nhật cài đặt (giá trị JSON: string/bool/number/array/object)")
            .RequireAuthorization("Admin");

        return app;
    }

    private static long? ActorId(HttpContext ctx)
        => ctx.User.FindFirst(CurrentUserService.ClaimUid)?.Value is { Length: > 0 } s && long.TryParse(s, out var v)
            ? v
            : null;

    private static string? ValidateTypes(Dictionary<string, JsonElement> settings)
    {
        if (settings.TryGetValue(SettingKeys.UploadMaxMb, out var maxMb)
            && (maxMb.ValueKind != JsonValueKind.Number || maxMb.GetInt32() is < 1 or > 500))
            return "upload.max_mb phải là số từ 1 đến 500.";
        if (settings.TryGetValue(SettingKeys.UploadAllowedExt, out var ext) && ext.ValueKind != JsonValueKind.Array)
            return "upload.allowed_ext phải là mảng chuỗi.";
        if (settings.TryGetValue(SettingKeys.AuthAutoApproveDomains, out var domains) && domains.ValueKind != JsonValueKind.Array)
            return "auth.auto_approve_domains phải là mảng chuỗi.";
        foreach (var (key, value) in settings)
        {
            if (key is SettingKeys.ContentRequireReview or SettingKeys.ContentShowAuthorPublic or SettingKeys.DownloadGuestDefault
                && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return $"{key} phải là true hoặc false.";
        }
        return null;
    }
}

public record SettingsRequest(Dictionary<string, JsonElement>? Settings);
