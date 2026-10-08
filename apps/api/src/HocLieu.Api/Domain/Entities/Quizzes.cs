namespace HocLieu.Domain.Entities;

public class Quiz
{
    public long Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? DescriptionHtml { get; set; }
    public long? SectionId { get; set; }
    public Section? Section { get; set; }
    public short? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public long? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public long? SchoolYearId { get; set; }
    public SchoolYear? SchoolYear { get; set; }
    public short? WeekNo { get; set; }
    public long? OwnerId { get; set; }
    public User? Owner { get; set; }
    public long? TeamId { get; set; }
    public Team? Team { get; set; }
    public ContentScope Scope { get; set; } = ContentScope.Public;
    public PublishMode PublishMode { get; set; } = PublishMode.Hidden;
    public DateTimeOffset? PublishFrom { get; set; }
    public DateTimeOffset? PublishUntil { get; set; }
    public ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Approved;
    public short? TimeLimitMinutes { get; set; }
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public ShowAnswers ShowAnswers { get; set; } = ShowAnswers.AfterSubmit;
    public IdentityMode IdentityMode { get; set; } = IdentityMode.Name;
    public short? MaxAttempts { get; set; }
    public MultiScoring MultiScoring { get; set; } = MultiScoring.AllOrNothing;
    public ScoreRounding ScoreRounding { get; set; } = ScoreRounding.Quarter;
    public long? SourceFileId { get; set; }
    public FileEntity? SourceFile { get; set; }
    public long? PrintFileId { get; set; }
    public FileEntity? PrintFile { get; set; }
    public string? ImportWarnings { get; set; } // jsonb
    public int QuestionCount { get; set; }
    public decimal TotalPoints { get; set; }
    public int AttemptCount { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Computed column (f_unaccent(lower(title + description không tag))) — tìm kiếm không dấu §9.</summary>
    public string? SearchText { get; set; }

    public ICollection<QuestionGroup> Groups { get; set; } = [];
    public ICollection<Question> Questions { get; set; } = [];
}

public class QuestionGroup
{
    public long Id { get; set; }
    public long QuizId { get; set; }
    public Quiz Quiz { get; set; } = default!;
    public short Sort { get; set; }
    public string? Title { get; set; }
    public string? PassageHtml { get; set; }
}

public class Question
{
    public long Id { get; set; }
    public long QuizId { get; set; }
    public Quiz Quiz { get; set; } = default!;
    public long? GroupId { get; set; }
    public QuestionGroup? Group { get; set; }
    public short Sort { get; set; }
    public QuestionType Type { get; set; } = QuestionType.Single;
    public string ContentHtml { get; set; } = default!;
    public string? ExplanationHtml { get; set; }
    public decimal Points { get; set; } = 1m;
    public string[]? AcceptedAnswers { get; set; } // cho câu ShortText (phase 2)

    public ICollection<QuestionOption> Options { get; set; } = [];
}

public class QuestionOption
{
    public long Id { get; set; }
    public long QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public short Sort { get; set; }
    public string ContentHtml { get; set; } = default!;
    public bool IsCorrect { get; set; }
}
