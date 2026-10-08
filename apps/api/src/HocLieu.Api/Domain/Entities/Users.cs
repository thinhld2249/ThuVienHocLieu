using System.ComponentModel.DataAnnotations;
using HocLieu.Common;

namespace HocLieu.Domain.Entities;

public class User
{
    public long Id { get; set; }
    public string Email { get; set; } = default!;
    public string? GoogleSub { get; set; }
    public string FullName { get; set; } = default!;
    public string? AvatarUrl { get; set; }
    public string? Phone { get; set; }
    /// <summary>Hash PBKDF2; null = tài khoản chưa đặt mật khẩu (chỉ đăng nhập Google).</summary>
    public string? PasswordHash { get; set; }
    public SystemRole SystemRole { get; set; } = SystemRole.Teacher;
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public string? StatusReason { get; set; }
    public long? RequestedTeamId { get; set; }
    public Team? RequestedTeam { get; set; }
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public long? ApprovedBy { get; set; }
    public User? Approver { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TeamMember> TeamMemberships { get; set; } = [];
}

public class Team
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public short? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class TeamMember
{
    public long TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public TeamRole Role { get; set; } = TeamRole.Member;
    public DateTimeOffset JoinedAt { get; set; }
}

public class Invitation
{
    public Guid Id { get; set; } = GuidV7.New();
    public string Email { get; set; } = default!;
    public long TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public TeamRole TeamRole { get; set; } = TeamRole.Member;
    public byte[] TokenHash { get; set; } = default!;
    // Token gốc (base64url) để UI hiện link /moi/{token}; xác thực vẫn so TokenHash (SHA-256)
    public string? TokenText { get; set; }
    public string? Message { get; set; }
    public long? InvitedBy { get; set; }
    public User? Inviter { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public long? AcceptedUserId { get; set; }
    public User? AcceptedUser { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
