using Google.Apis.Auth;

namespace HocLieu.Common.Auth;

public record GoogleIdentity(string Sub, string Email, bool EmailVerified, string Name, string? AvatarUrl, string? Phone);

public interface IGoogleTokenValidator
{
    /// <summary>Trả về identity hợp lệ hoặc null; message cho biết lý do từ chối.</summary>
    Task<(GoogleIdentity? Identity, string? Error)> ValidateAsync(string idToken, string audience, CancellationToken ct = default);
}

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    public async Task<(GoogleIdentity? Identity, string? Error)> ValidateAsync(string idToken, string audience, CancellationToken ct = default)
    {
        try
        {
            // GoogleJsonWebSignature tự kiểm tra issuer của Google; chỉ cần audience (client id)
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [audience],
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return (new GoogleIdentity(
                        payload.Subject,
                        payload.Email ?? string.Empty,
                        payload.EmailVerified,
                        payload.Name ?? payload.Email ?? "Người dùng Google",
                        payload.Picture,
                        null),
                    null);
        }
        catch (InvalidJwtException)
        {
            return (null, "Không xác thực được đăng nhập Google. Hãy thử lại hoặc đăng nhập bằng trình duyệt khác.");
        }
        catch (Exception)
        {
            return (null, "Phiên đăng nhập Google hết hạn. Hãy thử đăng nhập lại.");
        }
    }
}

/// <summary>Dùng trong test (spec §16): inject để tạo user với email tùy ý, không gọi Google thật.</summary>
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public Dictionary<string, GoogleIdentity> Identities { get; } = [];

    public Task<(GoogleIdentity? Identity, string? Error)> ValidateAsync(string idToken, string audience, CancellationToken ct = default)
        => Task.FromResult(Identities.TryGetValue(idToken, out var identity)
            ? (identity, (string?)null)
            : ((GoogleIdentity?)null, "Token test không hợp lệ"));
}

/// <summary>
/// Chế độ dev/E2E (spec §16 "đăng nhập giả lập"): chỉ kích hoạt khi env `Auth__DevFakeGoogle=true`.
/// idToken dạng `devfake:{sub}:{email}[:{name}]` — không gọi Google thật.
/// Bọc validator thật: chỉ token tiền tố `devfake:` đi đường giả lập, mọi token khác delegate
/// cho Google → dev vừa đăng nhập Google thật vừa đăng nhập nhanh dev/E2E (decisions.md 2026-10-09).
/// Không bao giờ bật ở prod (compose không set biến này).
/// </summary>
public sealed class DevFakeGoogleTokenValidator(IGoogleTokenValidator fallback) : IGoogleTokenValidator
{
    public Task<(GoogleIdentity? Identity, string? Error)> ValidateAsync(string idToken, string audience, CancellationToken ct = default)
    {
        if (!idToken.StartsWith("devfake:", StringComparison.Ordinal))
            return fallback.ValidateAsync(idToken, audience, ct);

        (GoogleIdentity? identity, string? error) result;
        var parts = idToken.Split(':', 4);
        if (parts.Length is < 3 or > 4 || parts[0] != "devfake" || string.IsNullOrWhiteSpace(parts[2]))
            result = (null, "Token dev không hợp lệ");
        else
        {
            var email = parts[2].Trim();
            var name = parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3])
                ? parts[3].Trim()
                : email;
            result = (new GoogleIdentity(parts[1].Trim(), email, true, name, null, null), null);
        }
        return Task.FromResult(result);
    }
}
