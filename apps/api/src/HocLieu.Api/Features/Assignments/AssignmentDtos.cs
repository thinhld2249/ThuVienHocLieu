namespace HocLieu.Features.Assignments;

// ===== Khu GV (spec §7, §10) =====

public sealed record CreateAssignmentRequest(
    long QuizId, bool UseRoster, DateTimeOffset? OpenAt, DateTimeOffset? CloseAt);

public sealed record UpdateAssignmentRequest(
    bool? UseRoster, DateTimeOffset? OpenAt, DateTimeOffset? CloseAt);

/// <summary>1 bài đã giao trong tab "Bài đã giao" của lớp.</summary>
public sealed record AssignmentDto(
    long Id,
    long QuizId,
    string QuizTitle,
    string Code,
    bool UseRoster,
    DateTimeOffset? OpenAt,
    DateTimeOffset? CloseAt,
    int AttemptCount,
    decimal? BestScore10,
    string CreatorName,
    DateTimeOffset CreatedAt);

// ===== Khu công khai — mã giao bài (spec §6.6, §7, §10) =====

public sealed record PublicRosterStudentDto(long Id, string FullName);

/// <summary>
/// Thông tin bài giao theo mã 6 ký tự. Trạng thái: Scheduled (chưa tới giờ mở) ·
/// Open (đang mở) · Closed (đã qua giờ đóng). Danh sách học sinh (id + fullName)
/// chỉ trả về khi use_roster VÀ đang mở (spec §7 — không lộ danh sách lớp).
/// </summary>
public sealed record PublicAssignmentDto(
    string Code,
    string Status,
    string QuizTitle,
    int QuestionCount,
    short? TimeLimitMinutes,
    string IdentityMode,
    short? MaxAttempts,
    int UsedAttempts,
    DateTimeOffset? OpenAt,
    DateTimeOffset? CloseAt,
    string ClassName,
    IReadOnlyList<PublicRosterStudentDto>? Roster);

/// <summary>BODY POST /api/public/assignments/{code}/attempts (spec §10).</summary>
public sealed record CreateAssignmentAttemptRequest(long? StudentId, string? GuestName);
