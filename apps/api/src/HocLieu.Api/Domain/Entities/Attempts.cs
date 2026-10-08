namespace HocLieu.Domain.Entities;

using HocLieu.Common;

public class Attempt
{
    public Guid Id { get; set; } = GuidV7.New();
    public long QuizId { get; set; }
    public Quiz Quiz { get; set; } = default!;
    public long? AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }
    public long? StudentId { get; set; }
    public Student? Student { get; set; }
    public string? GuestName { get; set; }
    public string? GuestClass { get; set; }
    public string? DeviceId { get; set; }
    public string? IpHash { get; set; }
    public string Layout { get; set; } = "{}"; // jsonb: thứ tự câu & phương án sau trộn
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal? Score { get; set; }
    public decimal? Score10 { get; set; }
    public int? CorrectCount { get; set; }
    public int? QuestionCount { get; set; }
    public int? DurationSec { get; set; }

    public ICollection<AttemptAnswer> Answers { get; set; } = [];
}

public class AttemptAnswer
{
    public Guid AttemptId { get; set; }
    public Attempt Attempt { get; set; } = default!;
    public long QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public long[] SelectedOptionIds { get; set; } = [];
    public string? TextAnswer { get; set; }
    public bool? IsCorrect { get; set; }
    public decimal? PointsAwarded { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
