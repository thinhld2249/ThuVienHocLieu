namespace HocLieu.Features.Quizzes;

// ===== Form tạo/sửa (FE gửi) =====

/// <summary>
/// POST /teacher/quizzes — tạo quiz trống (Hidden, spec §4.4) hoặc kèm câu hỏi mẫu.
/// Câu hỏi/phiên bản đầy đủ dùng chung <see cref="UpdateQuizRequest"/>.
/// </summary>
public sealed record CreateQuizRequest(
    string? Title,
    long? SectionId,
    short? GradeId,
    long? SubjectId,
    long? SchoolYearId,
    short? WeekNo,
    string? Scope,
    long? TeamId,
    string? DescriptionHtml,
    long? PrintFileId,
    QuizSettings? Settings);

/// <summary>Cài đặt làm bài (spec §6.5 tab Cài đặt). Field null = giữ mặc định hệ thống.</summary>
public sealed record QuizSettings(
    short? TimeLimitMinutes,
    bool? ShuffleQuestions,
    bool? ShuffleOptions,
    string? ShowAnswers,
    string? IdentityMode,
    short? MaxAttempts,
    string? MultiScoring,
    string? ScoreRounding);

/// <summary>PUT /teacher/quizzes/{id} — thay toàn bộ: câu có id = cập nhật, không id = thêm, thiếu = xóa.</summary>
public sealed record UpdateQuizRequest(
    string? Title,
    long? SectionId,
    short? GradeId,
    long? SubjectId,
    long? SchoolYearId,
    short? WeekNo,
    string? Scope,
    long? TeamId,
    string? DescriptionHtml,
    long? PrintFileId,
    QuizSettings? Settings,
    IReadOnlyList<QuizGroupInput>? Groups,
    IReadOnlyList<QuizQuestionInput>? Questions,
    string? UpdatedAt);

public sealed record QuizGroupInput(long? Id, short? Sort, string? Title, string? PassageHtml);

public sealed record QuizQuestionInput(
    long? Id,
    short? Sort,
    long? GroupId,
    string? Type,
    string? ContentHtml,
    string? ExplanationHtml,
    decimal? Points,
    IReadOnlyList<QuizOptionInput>? Options);

public sealed record QuizOptionInput(long? Id, short? Sort, string? ContentHtml, bool? IsCorrect);

// ===== Danh sách / chi tiết (khu GV) =====

/// <summary>1 hàng trong bảng "Bài tập của tôi" (/gv/bai-tap).</summary>
public sealed record MyQuizRow(
    long Id, string Title, string Slug,
    string? SectionSlug, string? SectionName,
    short? Grade, string? SubjectName,
    string? SchoolYearName, short? WeekNo,
    string Scope, string PublishMode,
    DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil, string PublishState,
    string ModerationStatus,
    int QuestionCount, decimal TotalPoints, int AttemptCount,
    bool HasImportWarnings,
    DateTimeOffset CreatedAt, string UpdatedAt, bool IsDeleted);

/// <summary>Chi tiết cho tabs của /gv/bai-tap/:id (editor + cài đặt + hiển thị).</summary>
public sealed record QuizDetailDto(
    long Id, string Title, string Slug, string? DescriptionHtml,
    long? SectionId, string? SectionSlug, string? SectionName,
    short? GradeId, string? GradeName,
    long? SubjectId, string? SubjectName,
    long? SchoolYearId, string? SchoolYearName,
    short? WeekNo,
    long? TeamId, string? TeamName,
    long? OwnerId, string? OwnerName,
    string Scope, string PublishMode,
    DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil, string PublishState,
    string ModerationStatus, string? ModerationNote,
    short? TimeLimitMinutes, bool ShuffleQuestions, bool ShuffleOptions,
    string ShowAnswers, string IdentityMode, short? MaxAttempts,
    string MultiScoring, string ScoreRounding,
    long? PrintFileId,
    string? ImportWarnings,
    int QuestionCount, decimal TotalPoints, int AttemptCount,
    IReadOnlyList<QuizGroupDto> Groups,
    IReadOnlyList<QuizQuestionDto> Questions,
    bool IsDeleted,
    DateTimeOffset CreatedAt, string UpdatedAt);

public sealed record QuizGroupDto(long Id, short Sort, string? Title, string? PassageHtml);

public sealed record QuizQuestionDto(
    long Id, short Sort, long? GroupId, string Type,
    string ContentHtml, string? ExplanationHtml, decimal Points,
    IReadOnlyList<QuizOptionDto> Options);

public sealed record QuizOptionDto(long Id, short Sort, string ContentHtml, bool IsCorrect);

/// <summary>Kết quả import: quiz đã tạo (Hidden) + danh sách cảnh báo cho màn rà soát.</summary>
public sealed record QuizImportResultDto(long QuizId, IReadOnlyList<QuizWarningDto> Warnings);

public sealed record QuizWarningDto(string Code, int? QuestionNumber, string Message);

// ===== Khu công khai =====

/// <summary>Giới thiệu quiz trước khi làm bài (spec §5.1 /bai-tap/:slug-:id). Không có câu hỏi.</summary>
public sealed record PublicQuizDetailDto(
    long Id, string Slug, string Title, string? DescriptionHtml,
    string? SectionSlug, string? SectionName,
    short? Grade, string? SubjectName, short? WeekNo, string? SchoolYearName,
    int QuestionCount, short? TimeLimitMinutes,
    string PublishState, DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil,
    string IdentityMode, short? MaxAttempts, int AttemptCount,
    long? PrintFileId,
    /// <summary>Lượt đang dở trên thiết bị này (cookie hl_dev) — FE hiện nút "Làm tiếp".</summary>
    string? InProgressAttemptId,
    string CreatedAt);
