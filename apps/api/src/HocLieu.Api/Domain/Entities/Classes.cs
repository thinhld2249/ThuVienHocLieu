namespace HocLieu.Domain.Entities;

public class ClassEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public short? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public long? SchoolYearId { get; set; }
    public SchoolYear? SchoolYear { get; set; }
    public long? TeamId { get; set; }
    public Team? Team { get; set; }
    public long? HomeroomTeacherId { get; set; }
    public User? HomeroomTeacher { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ClassTeacher> Teachers { get; set; } = [];
    public ICollection<Student> Students { get; set; } = [];
}

public class ClassTeacher
{
    public long ClassId { get; set; }
    public ClassEntity Class { get; set; } = default!;
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public long? SubjectId { get; set; }
    public Subject? Subject { get; set; }
}

public class Student
{
    public long Id { get; set; }
    public long ClassId { get; set; }
    public ClassEntity Class { get; set; } = default!;
    public short? Ordinal { get; set; }
    public string FullName { get; set; } = default!;
    public string? StudentCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Assignment
{
    public long Id { get; set; }
    public long QuizId { get; set; }
    public Quiz Quiz { get; set; } = default!;
    public long ClassId { get; set; }
    public ClassEntity Class { get; set; } = default!;
    public string Code { get; set; } = default!;
    public bool UseRoster { get; set; } = true;
    public DateTimeOffset? OpenAt { get; set; }
    public DateTimeOffset? CloseAt { get; set; }
    public long CreatedBy { get; set; }
    public User? Creator { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
