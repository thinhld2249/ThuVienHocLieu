namespace HocLieu.Features.Attempts;

// ===== Khu công khai (spec §6.6–6.7, §10) =====
// Quy tắc an toàn (spec §12): DTO công khai là các type riêng, không tái dùng entity/DTO của GV;
// KHÔNG BAO GIỜ chứa isCorrect/explanationHtml trước khi show_answers cho phép.

public sealed record CreateAttemptRequest(string? GuestName, string? GuestClass);

public sealed record SaveAnswerItem(long QuestionId, IReadOnlyList<long>? OptionIds);

/// <summary>Câu hỏi trong giao diện làm bài — không đáp án đúng, không giải thích.</summary>
public sealed record AttemptQuestionDto(
    long Id, long? GroupId, int Number, string Type,
    string ContentHtml, IReadOnlyList<AttemptOptionDto> Options);

public sealed record AttemptOptionDto(long Id, string ContentHtml);

/// <summary>Trả về POST create / GET attempt: InProgress (câu hỏi + câu trả lời đã lưu)
/// hoặc đã nộp/hết giờ (kết quả theo show_answers).</summary>
public sealed record AttemptDto(
    string Id,
    string Status,
    DateTimeOffset? ExpiresAt,
    string Title,
    short? TimeLimitMinutes,
    int QuestionCount,
    string IdentityMode,
    short? MaxAttempts,
    int UsedAttempts,
    string GuestName,
    string GuestClass,
    IReadOnlyList<AttemptGroupDto> Groups,
    IReadOnlyList<AttemptQuestionDto> Questions,
    IReadOnlyList<AttemptSavedAnswerDto> Answers,
    AttemptResultDto? Result);

public sealed record AttemptGroupDto(long Id, string? Title, string? PassageHtml);

public sealed record AttemptSavedAnswerDto(long QuestionId, IReadOnlyList<long> OptionIds);

/// <summary>Kết quả (chấm ở server, spec §6.7). Review = null khi show_answers chưa cho phép xem đáp án.</summary>
public sealed record AttemptResultDto(
    decimal Score10,
    decimal Score,
    decimal TotalPoints,
    int? CorrectCount,
    int? QuestionCount,
    int? DurationSec,
    DateTimeOffset SubmittedAt,
    bool CanReview,
    IReadOnlyList<AttemptReviewQuestionDto>? Review);

public sealed record AttemptReviewQuestionDto(
    long Id, int Number, string ContentHtml,
    IReadOnlyList<AttemptReviewOptionDto> Options,
    string? ExplanationHtml);

public sealed record AttemptReviewOptionDto(
    long Id, string ContentHtml, bool Selected, bool IsCorrect);

// ===== Khu GV (spec §6.8) =====

/// <summary>1 lượt làm trong bảng kết quả.</summary>
public sealed record AttemptRowDto(
    string Id,
    string? StudentName,
    string? StudentClass,
    string? GuestName,
    string Status,
    decimal? Score10,
    int? CorrectCount,
    int? QuestionCount,
    int? DurationSec,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    int AttemptNo);

/// <summary>Thống kê cho tab Thống kê (spec §6.8).</summary>
public sealed record QuizStatsDto(
    int AttemptCount,
    decimal? AverageScore10,
    decimal? MedianScore10,
    int? MinScore10,
    int? MaxScore10,
    IReadOnlyList<ScoreBucket> Distribution,
    IReadOnlyList<QuestionStatRow> Questions);

public sealed record ScoreBucket(int Score10, int Count); // bucket 0..10 (điểm đã làm tròn 0.25 → gộp theo số nguyên gần nhất)

public sealed record QuestionStatRow(
    long Id, int Number, string Preview,
    int Attempted, int CorrectCount, decimal CorrectPercent,
    IReadOnlyList<OptionPickRow> OptionPicks);

public sealed record OptionPickRow(long OptionId, string Preview, int Picks, bool IsCorrect);
