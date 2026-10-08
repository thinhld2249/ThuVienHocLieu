namespace HocLieu.Features.Classes;

public sealed record ClassTeacherDto(long UserId, string FullName, long? SubjectId, string? SubjectName);

public sealed record ClassDto(
    long Id,
    string Name,
    short? GradeId,
    string? GradeName,
    long? SchoolYearId,
    string? SchoolYearName,
    long? TeamId,
    string? TeamName,
    long? HomeroomTeacherId,
    string? HomeroomTeacherName,
    IReadOnlyList<ClassTeacherDto> Teachers,
    int StudentCount,
    int AssignmentCount,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateClassRequest(
    string Name,
    short? GradeId,
    long? SchoolYearId,
    long? TeamId,
    IReadOnlyList<ClassTeacherItem>? Teachers);

public sealed record ClassTeacherItem(long UserId, long? SubjectId);

public sealed record UpdateClassRequest(
    string? Name,
    short? GradeId,
    long? SchoolYearId,
    long? TeamId,
    IReadOnlyList<ClassTeacherItem>? Teachers);

public sealed record StudentDto(
    long Id,
    short? Ordinal,
    string FullName,
    string? StudentCode,
    DateOnly? DateOfBirth,
    string? Gender,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateStudentRequest(
    string FullName,
    string? StudentCode,
    DateOnly? DateOfBirth,
    string? Gender);

public sealed record UpdateStudentRequest(
    string? FullName,
    string? StudentCode,
    DateOnly? DateOfBirth,
    string? Gender,
    short? Ordinal,
    bool? IsActive);

/// <summary>
/// §7: nhập danh sách HS từ Excel. Mỗi dòng lỗi có số thứ tự dòng trong file (bắt đầu từ 2 — dòng 1 là tiêu đề).
/// </summary>
public sealed record StudentImportRowError(int Row, string Message);

public sealed record StudentImportResult(
    bool DryRun,
    int TotalRows,
    int ImportCount,
    int DuplicateCount,
    IReadOnlyList<StudentImportRowError> Errors);

public sealed record AssignmentClassRowDto(
    long Id,
    string QuizTitle,
    string Code,
    bool UseRoster,
    DateTimeOffset? OpenAt,
    DateTimeOffset? CloseAt,
    int AttemptCount,
    decimal? BestScore10,
    DateTimeOffset CreatedAt);

public sealed record GradebookStudentRow(long StudentId, string FullName);

public sealed record GradebookAssignmentColumn(long AssignmentId, string QuizTitle, string Code, DateTimeOffset CreatedAt);

/// <summary>Hàng = học sinh, cột = bài đã giao, ô = điểm cao nhất (null = chưa làm). spec §7.</summary>
public sealed record GradebookDto(
    string ClassName,
    IReadOnlyList<GradebookAssignmentColumn> Columns,
    IReadOnlyList<GradebookStudentRow> Students,
    IReadOnlyList<IReadOnlyList<decimal?>> Scores);
