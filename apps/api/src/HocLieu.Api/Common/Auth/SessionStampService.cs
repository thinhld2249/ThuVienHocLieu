using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;

namespace HocLieu.Common.Auth;

/// <summary>
/// §3.4: security_stamp đổi khi khóa tài khoản, đổi vai trò, gỡ khỏi tổ, "đăng xuất mọi thiết bị".
/// OnValidatePrincipal so stamp cookie với DB (cache bộ nhớ 60 giây) → lệch thì hủy phiên.
/// </summary>
public sealed class SessionStampService(AppDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheLife = TimeSpan.FromSeconds(60);

    public async Task<bool> IsSessionValidAsync(long userId, Guid stamp, CancellationToken ct = default)
    {
        var user = await GetUserAsync(userId, ct);
        if (user is null)
            return false;
        // Suspended/Rejected không giữ phiên
        if (user.Status is Domain.UserStatus.Suspended or Domain.UserStatus.Rejected)
            return false;
        return user.SecurityStamp == stamp;
    }

    public async Task InvalidateUserAsync(long userId, CancellationToken ct = default)
    {
        cache.Remove($"stamp:{userId}");
        await Task.CompletedTask;
    }

    private async Task<User?> GetUserAsync(long userId, CancellationToken ct)
    {
        var key = $"stamp:{userId}";
        if (cache.TryGetValue<User>(key, out var cached))
            return cached;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
            cache.Set(key, user, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLife });
        return user;
    }
}
