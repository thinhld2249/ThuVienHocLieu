using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Auth;

/// <summary>
/// Luồng đăng nhập Google (spec §3.1) + /me + vòng đời phiên (spec §3.4).
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    IGoogleTokenValidator google,
    IAuditLogger audit,
    IConfiguration config,
    TimeProvider time)
{
    private string GoogleClientId => config["Auth:GoogleClientId"] ?? string.Empty;
    private string[] AdminEmails => (config["Auth:AdminEmails"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(e => e.ToLowerInvariant())
        .ToArray();
    private string[] AllowedDomains => (config["Auth:AllowedDomains"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(d => d.ToLowerInvariant())
        .ToArray();

    /// <summary>§3.1: xác thực idToken → tìm/tạo user → cấp quyền theo trạng thái → trả principal. Lỗi ném <see cref="AuthFlowException"/>.</summary>
    public async Task<ClaimsPrincipal> LoginAsync(GoogleLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.IdToken))
            throw new AuthFlowException(401, "auth.invalid_token", "Thiếu mã đăng nhập Google.");

        var (identity, error) = await google.ValidateAsync(req.IdToken, GoogleClientId, ct);
        if (identity is null || !identity.EmailVerified)
            throw new AuthFlowException(401, "auth.invalid_token", error ?? "Không xác thực được đăng nhập Google.");

        if (AllowedDomains.Length > 0)
        {
            var domain = EmailDomain(identity.Email);
            if (!AllowedDomains.Contains(domain))
                throw new AuthFlowException(403, "auth.domain_not_allowed",
                    "Tài khoản Google này không được phép đăng nhập vào hệ thống.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSub == identity.Sub, ct);
        if (user is null)
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == identity.Email, ct); // citext: không phân biệt hoa thường

        // §3.3: lời mời — qua token (phải khớp email) hoặc email trùng lời mời đang chờ
        Invitation? invite = null;
        if (!string.IsNullOrWhiteSpace(req.InviteToken))
        {
            var hash = HashToken(req.InviteToken!);
            invite = await db.Invitations.Include(i => i.Team)
                .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
            if (invite is null || invite.RevokedAt is not null || invite.AcceptedAt is not null || invite.ExpiresAt < time.GetUtcNow())
                throw new AuthFlowException(400, "invite.invalid", "Lời mời không hợp lệ hoặc đã hết hạn.");
            if (!string.Equals(invite.Email, identity.Email, StringComparison.OrdinalIgnoreCase))
                throw new AuthFlowException(400, "invite.email_mismatch",
                    $"Lời mời này dành cho {MaskEmail(invite.Email)}.");
        }
        else if (user is null || user.Status == UserStatus.Pending)
        {
            // §3.1.5: email trùng một lời mời đang chờ (GV mở link nhưng không kịp truyền token)
            invite = await db.Invitations.Include(i => i.Team)
                .FirstOrDefaultAsync(i => i.Email == identity.Email
                    && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > time.GetUtcNow(), ct);
        }

        if (user is null)
        {
            // §3.1.5: user mới — Admin / lời mời / domain tự duyệt / Pending
            var status = UserStatus.Pending;
            var role = SystemRole.Teacher;
            if (AdminEmails.Contains(identity.Email.ToLowerInvariant()))
            {
                role = SystemRole.Admin;
                status = UserStatus.Active;
            }
            else if (invite is not null)
                status = UserStatus.Active;
            else if (await IsAutoApproveDomainAsync(EmailDomain(identity.Email), ct))
                status = UserStatus.Active;

            user = new User
            {
                Email = identity.Email,
                GoogleSub = identity.Sub,
                FullName = identity.Name,
                AvatarUrl = identity.AvatarUrl,
                SystemRole = role,
                Status = status,
            };
            db.Users.Add(user);
            if (invite is not null)
            {
                await EnsureLeadSlotAsync(invite.TeamId, invite.TeamRole, ct);
                // user mới chưa có ID (bigint identity) → gán qua navigation, EF tự fix FK
                db.TeamMembers.Add(new TeamMember
                {
                    TeamId = invite.TeamId,
                    User = user,
                    Role = invite.TeamRole,
                    JoinedAt = time.GetUtcNow(),
                });
                invite.AcceptedAt = time.GetUtcNow();
                invite.AcceptedUser = user;
            }
        }
        else
        {
            // §3.2: tài khoản đã khóa/từ chối không cấp phiên
            if (user.Status == UserStatus.Suspended)
                throw new AuthFlowException(403, ErrorCodes.SuspendedAccount,
                    string.IsNullOrWhiteSpace(user.StatusReason)
                        ? "Tài khoản của bạn đang bị khóa. Liên hệ quản trị viên để được hỗ trợ."
                        : $"Tài khoản của bạn đang bị khóa: {user.StatusReason}");
            if (user.Status == UserStatus.Rejected)
                throw new AuthFlowException(403, ErrorCodes.RejectedAccount,
                    string.IsNullOrWhiteSpace(user.StatusReason)
                        ? "Yêu cầu tham gia của bạn đã bị từ chối. Liên hệ quản trị viên để được hỗ trợ."
                        : $"Yêu cầu tham gia của bạn đã bị từ chối: {user.StatusReason}");

            user.GoogleSub = identity.Sub;
            user.FullName = identity.Name;
            user.AvatarUrl = identity.AvatarUrl;
            if (user.Status == UserStatus.Pending && invite is not null)
            {
                // §3.3: lời mời hợp lệ cho user đang chờ → duyệt luôn + vào tổ
                user.Status = UserStatus.Active;
                user.ApprovedAt = time.GetUtcNow();
                await EnsureLeadSlotAsync(invite.TeamId, invite.TeamRole, ct);
                db.TeamMembers.Add(new TeamMember
                {
                    TeamId = invite.TeamId,
                    UserId = user.Id,
                    Role = invite.TeamRole,
                    JoinedAt = time.GetUtcNow(),
                });
                invite.AcceptedAt = time.GetUtcNow();
                invite.AcceptedUserId = user.Id;
            }
        }

        user.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.login", "user", user.Id.ToString(),
            new { email = user.Email, status = user.Status.ToString() }, ct);

        return BuildPrincipal(user);
    }

    /// <summary>
    /// Đăng nhập email + mật khẩu (bổ sung ngoài spec — chủ hệ thống yêu cầu).
    /// Chỉ tài khoản đã đặt mật khẩu (PasswordHash != null) đăng nhập được;
    /// Google vẫn là luồng chính. Trạng thái tài khoản xử lý như luồng Google.
    /// </summary>
    public async Task<ClaimsPrincipal> LoginWithPasswordAsync(PasswordLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
            throw new AuthFlowException(401, "auth.bad_credentials", "Vui lòng nhập email và mật khẩu.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim(), ct);
        if (user is null)
            throw new AuthFlowException(401, "auth.bad_credentials", "Email hoặc mật khẩu không đúng.");
        if (user.PasswordHash is null)
            throw new AuthFlowException(400, "auth.no_password",
                "Tài khoản này chưa đặt mật khẩu. Hãy đăng nhập bằng Google rồi đặt mật khẩu ở trang Hồ sơ.");
        if (!PasswordHasher.Verify(req.Password, user.PasswordHash))
            throw new AuthFlowException(401, "auth.bad_credentials", "Email hoặc mật khẩu không đúng.");

        if (user.Status == UserStatus.Suspended)
            throw new AuthFlowException(403, ErrorCodes.SuspendedAccount,
                string.IsNullOrWhiteSpace(user.StatusReason)
                    ? "Tài khoản của bạn đang bị khóa. Liên hệ quản trị viên để được hỗ trợ."
                    : $"Tài khoản của bạn đang bị khóa: {user.StatusReason}");
        if (user.Status == UserStatus.Rejected)
            throw new AuthFlowException(403, ErrorCodes.RejectedAccount,
                string.IsNullOrWhiteSpace(user.StatusReason)
                    ? "Yêu cầu tham gia của bạn đã bị từ chối. Liên hệ quản trị viên để được hỗ trợ."
                    : $"Yêu cầu tham gia của bạn đã bị từ chối: {user.StatusReason}");

        user.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.login", "user", user.Id.ToString(),
            new { email = user.Email, status = user.Status.ToString(), method = "password" }, ct);

        return BuildPrincipal(user);
    }

    /// <summary>Đặt/đổi mật khẩu đăng nhập của chính mình (trang Hồ sơ).</summary>
    public async Task SetPasswordAsync(long userId, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword.Length > 128)
            throw new AuthFlowException(422, "validation", "Mật khẩu phải từ 8 đến 128 ký tự.");

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.password_set", "user", userId.ToString());
    }

    public static ClaimsPrincipal BuildPrincipal(User user)
    {
        var claims = new List<Claim>
        {
            new(CurrentUserService.ClaimUid, user.Id.ToString()),
            new(CurrentUserService.ClaimStamp, user.SecurityStamp.ToString()),
            new("status", user.Status.ToString()),
            new("system_role", user.SystemRole.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
        };
        // authenticationType != null → IsAuthenticated = true (CookieSignIn yêu cầu,
        // RequireAuthenticatedSignIn mặc định true)
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name, ClaimTypes.Email);
        return new ClaimsPrincipal(identity);
    }

    public async Task<MeDto?> GetMeAsync(long userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;
        return await ToMeDtoAsync(user, ct);
    }

    /// <summary>§3.1: Pending điền lại họ tên/SĐT + chọn tổ chờ duyệt; Active sửa họ tên/SĐT.</summary>
    public async Task<MeDto?> UpdateMeAsync(long userId, UpdateMeRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

        if (!string.IsNullOrWhiteSpace(req.FullName))
        {
            var name = req.FullName.Trim();
            if (name.Length < 2 || name.Length > 200)
                throw new AuthFlowException(422, "validation", "Họ tên phải từ 2 đến 200 ký tự.");
            user.FullName = name;
        }

        if (req.Phone is not null)
        {
            var phone = req.Phone.Trim();
            if (phone.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?\d{6,15}$"))
                throw new AuthFlowException(422, "validation", "Số điện thoại không hợp lệ.");
            user.Phone = phone.Length > 0 ? phone : null;
        }

        if (req.RequestedTeamId is not null)
        {
            if (user.Status != UserStatus.Pending)
                throw new AuthFlowException(400, "user.not_pending", "Chỉ tài khoản đang chờ duyệt có thể gửi yêu cầu vào tổ.");
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == req.RequestedTeamId.Value && t.IsActive, ct);
            if (team is null)
                throw new AuthFlowException(422, "validation", "Tổ không tồn tại hoặc đã ngừng hoạt động.");
            user.RequestedTeamId = team.Id;
        }

        user.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await ToMeDtoAsync(user, ct);
    }

    /// <summary>§3.4: đổi security_stamp → mọi cookie cũ mất hiệu lực (≤ 60s do cache).</summary>
    public async Task LogoutAllAsync(long userId, SessionStampService stamps, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.SecurityStamp = GuidV7.New();
        user.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await stamps.InvalidateUserAsync(userId, ct);
        await audit.LogAsync("auth.logout_all", "user", userId.ToString());
    }

    /// <summary>§3.3: thông tin lời mời công khai (trang /moi/:token).</summary>
    public async Task<InvitationInfoDto?> GetInvitationAsync(string token, CancellationToken ct)
    {
        var hash = HashToken(token);
        var invite = await db.Invitations.Include(i => i.Team)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null)
            return null;

        var now = time.GetUtcNow();
        string status = invite.RevokedAt is not null ? "Đã thu hồi"
            : invite.AcceptedAt is not null ? "Đã nhận"
            : invite.ExpiresAt < now ? "Hết hạn"
            : "Chờ";
        return new InvitationInfoDto(MaskEmail(invite.Email), invite.Team.Name, invite.ExpiresAt, status);
    }

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1)
            return email;
        return email[0] + "***" + email[at..];
    }

    public static byte[] HashToken(string token)
        => SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));

    public async Task<bool> IsAutoApproveDomainAsync(string domain, CancellationToken ct)
    {
        var setting = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "auth.auto_approve_domains", ct);
        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
            return false;
        try
        {
            var domains = JsonSerializer.Deserialize<string[]>(setting.Value);
            return domains?.Contains(domain, StringComparer.OrdinalIgnoreCase) ?? false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string EmailDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "";
    }

    /// <summary>
    /// §2.1: mỗi tổ tối đa 1 Lead (unique index ux_team_one_lead).
    /// Khi lời mời mang vai trò Lead được chấp nhận, hạ Lead hiện tại về Member.
    /// </summary>
    private async Task EnsureLeadSlotAsync(long teamId, TeamRole role, CancellationToken ct)
    {
        if (role != TeamRole.Lead)
            return;
        var currentLead = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.Role == TeamRole.Lead, ct);
        if (currentLead is not null)
            currentLead.Role = TeamRole.Member;
    }

    private async Task<MeDto> ToMeDtoAsync(User user, CancellationToken ct)
    {
        var memberships = await db.TeamMembers.AsNoTracking()
            .Where(tm => tm.UserId == user.Id)
            .Select(tm => new { tm.TeamId, tm.Role, tm.Team.Name })
            .ToListAsync(ct);
        return new MeDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Phone,
            user.AvatarUrl,
            user.SystemRole.ToString(),
            user.Status.ToString(),
            user.StatusReason,
            memberships.Select(m => new MeTeamDto(m.TeamId, m.Name, m.Role.ToString())).ToList(),
            user.RequestedTeamId);
    }
}

/// <summary>Lỗi luồng auth với HTTP status + code máy đọc → endpoint chuyển thành ProblemDetails.</summary>
public sealed class AuthFlowException(int status, string code, string title) : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
}
