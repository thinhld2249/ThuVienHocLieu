namespace HocLieu.Features.Teams;

public record TeamDto(
    long Id, string Name, string? Description, string? GradeName,
    int MemberCount, string? LeadName, bool IsActive);

/// <summary>Chi tiết tổ cho người xem (role = vai trò của viewer trong tổ; Admin → null).</summary>
public record TeamDetailDto(
    long Id, string Name, string? Description, string? GradeName,
    int MemberCount, string? LeadName, string? Role, bool IsAdmin);

public record TeamMemberDto(
    long UserId, string FullName, string Email, string? AvatarUrl,
    string Role, DateTimeOffset JoinedAt, int ContentCount);

public record JoinRequestDto(
    long UserId, string FullName, string Email, string? AvatarUrl,
    string? Phone, DateTimeOffset RequestedAt);

public record JoinActionResult(long UserId, string FullName, bool Ok, string? Message);

/// <summary>Status: "Chờ" | "Đã nhận" | "Hết hạn" | "Đã thu hồi" (khớp GetInvitationAsync M1).</summary>
public record InvitationDto(
    Guid Id, string Email, string Status, string? Link,
    DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt,
    string TeamRole, string? InviterName, string? AcceptedByName,
    string? TeamName);

/// <summary>Action: "added" (Active vào thẳng) · "approved" (Pending được duyệt) · "invited" (tạo lời mời) · "existing" (đã là thành viên) · "existing_invite" (đã có lời mời chờ) · "skipped" (bỏ qua, xem Reason).</summary>
public record InvitationResult(
    string Email, string Action, string? Reason, Guid? InvitationId, string? Link);

public record TeamAnnouncementDto(
    long Id, string Title, string BodyHtml, bool IsPinned,
    string AuthorName, DateTimeOffset CreatedAt);

public record TeamStatsDto(
    int MemberCount, int DocumentCount, int QuizCount,
    List<TeamMemberStatDto> Members);

public record TeamMemberStatDto(
    long UserId, string FullName, string Role, int DocumentCount, int QuizCount);

// ===== Request bodies =====

public record CreateTeamRequest(string Name, string? Description, short? GradeId);

public record UpdateTeamRequest(string? Name, string? Description, short? GradeId, bool? IsActive);

public record SetLeadRequest(long UserId);

/// <summary>Role: "Member" | "Deputy" (tổ trưởng chỉ đổi qua /api/admin/teams/{id}/lead).</summary>
public record SetMemberRoleRequest(string Role);

public record RejectJoinRequestRequest(string? Reason);

public record BulkApproveRequest(List<long> UserIds);

public record CreateInvitationsRequest(List<string> Emails, string? Message);

/// <summary>Admin: teamRole bất kỳ ("Member" | "Deputy" | "Lead").</summary>
public record AdminCreateInvitationsRequest(long TeamId, string? TeamRole, List<string> Emails, string? Message);

public record PostAnnouncementRequest(string Title, string BodyHtml, bool IsPinned, DateTimeOffset? ExpireAt);
