namespace HocLieu.Features.Auth;

public record GoogleLoginRequest(string IdToken, string? InviteToken);

public record PasswordLoginRequest(string Email, string Password);

public record SetPasswordRequest(string NewPassword);

public record MeTeamDto(long Id, string Name, string Role);

public record MeDto(
    long Id,
    string Email,
    string FullName,
    string? Phone,
    string? AvatarUrl,
    string SystemRole,
    string Status,
    string? StatusReason,
    List<MeTeamDto> Teams,
    long? RequestedTeamId);

public record UpdateMeRequest(string? FullName, string? Phone, long? RequestedTeamId);

public record InvitationInfoDto(string EmailMasked, string TeamName, DateTimeOffset ExpiresAt, string Status);
