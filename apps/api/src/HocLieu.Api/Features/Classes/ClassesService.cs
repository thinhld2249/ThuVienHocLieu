using System.Globalization;
using ClosedXML.Excel;
using HocLieu.Common;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Classes;

public sealed class ClassValidationException(Dictionary<string, string[]> errors) : Exception("Dữ liệu không hợp lệ")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Đã thấy lớp nhưng viewer không có quyền (endpoint trả 403, không 404 — lớp tồn tại).</summary>
public sealed class ClassForbiddenException : Exception;

/// <summary>Lớp không tồn tại (endpoint trả 404).</summary>
public sealed class ClassNotFoundException : Exception;

/// <summary>
/// §7: lớp học, học sinh. Quyền truy cập lớp (CanAccessClass, spec §2.3):
/// GV chủ nhiệm ∨ GV bộ môn ∨ Lead|Deputy của tổ quản lý lớp ∨ Admin.
/// Xóa lớp/học sinh = xóa cứng (kèm lượt làm) sau xác nhận phía FE.
/// </summary>
public class ClassesService(AppDbContext db, TimeProvider time, IAuditLogger audit)
{
    private static readonly string[] DobFormats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy"];

    // ===== Truy cập =====

    public static bool CanAccessClass(ViewerContext viewer, ClassEntity cls, IReadOnlySet<long> teacherClassIds)
        => viewer.IsAdmin
           || cls.HomeroomTeacherId == viewer.UserId
           || (cls.TeamId is { } t && viewer.LeadOrDeputyTeamIds.Contains(t))
           || teacherClassIds.Contains(cls.Id);

    /// <summary>Các lớp người dùng là GV bộ môn (qua class_teachers).</summary>
    private async Task<IReadOnlySet<long>> MyClassIdsAsync(long userId, CancellationToken ct)
    {
        var ids = await db.ClassTeachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.ClassId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private async Task<ClassEntity?> GetAsync(long classId, CancellationToken ct)
        => await db.Classes
            .Include(c => c.Grade)
            .Include(c => c.SchoolYear)
            .Include(c => c.Team)
            .Include(c => c.HomeroomTeacher)
            .Include(c => c.Teachers).ThenInclude(t => t.User)
            .Include(c => c.Teachers).ThenInclude(t => t.Subject)
            .FirstOrDefaultAsync(c => c.Id == classId, ct);

    /// <summary>Trả null = không tồn tại (404); ném ClassForbiddenException khi không có quyền (403).</summary>
    public async Task<ClassEntity?> GetCheckedAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetAsync(classId, ct);
        if (cls is null)
            return null;
        if (viewer.UserId is not { } userId)
            throw new ClassForbiddenException();
        var myClassIds = await MyClassIdsAsync(userId, ct);
        if (!CanAccessClass(viewer, cls, myClassIds))
            throw new ClassForbiddenException();
        return cls;
    }

    /// <summary>Chi tiết lớp (kèm số HS / bài đã giao). null = 404.</summary>
    public async Task<ClassDto?> GetDtoAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;
        var studentCount = await db.Students.CountAsync(s => s.ClassId == classId, ct);
        var assignmentCount = await db.Assignments.CountAsync(a => a.ClassId == classId, ct);
        var fresh = await GetAsync(classId, ct);
        return fresh is null ? null : ToDto(fresh, studentCount, assignmentCount);
    }

    // ===== Lớp =====

    public async Task<IReadOnlyList<ClassDto>> ListMineAsync(ViewerContext viewer, CancellationToken ct)
    {
        var classes = await db.Classes
            .Include(c => c.Grade)
            .Include(c => c.SchoolYear)
            .Include(c => c.Team)
            .Include(c => c.HomeroomTeacher)
            .Include(c => c.Teachers).ThenInclude(t => t.User)
            .Include(c => c.Teachers).ThenInclude(t => t.Subject)
            .Include(c => c.Students)
            .OrderBy(c => c.SchoolYearId ?? 0)
            .ThenBy(c => c.GradeId ?? 0)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        var assignmentCounts = await db.Assignments.AsNoTracking()
            .GroupBy(a => a.ClassId)
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClassId, x => x.Count, ct);

        IReadOnlySet<long> myClassIds = new HashSet<long>();
        if (viewer.UserId is { } userId && !viewer.IsAdmin)
            myClassIds = await MyClassIdsAsync(userId, ct);

        return classes
            .Where(c => viewer.IsAdmin
                        || c.HomeroomTeacherId == viewer.UserId
                        || (c.TeamId is { } t && viewer.LeadOrDeputyTeamIds.Contains(t))
                        || myClassIds.Contains(c.Id))
            .Select(c => ToDto(c, c.Students.Count, assignmentCounts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<ClassDto> CreateAsync(ViewerContext viewer, CreateClassRequest req, CancellationToken ct)
    {
        if (viewer.UserId is not { } userId)
            throw new ClassValidationException(new() { ["name"] = ["Cần đăng nhập để tạo lớp."] });
        var now = time.GetUtcNow();

        var name = req.Name.Trim();
        var yearId = req.SchoolYearId ?? await db.SchoolYears
            .Where(y => y.IsCurrent)
            .Select(y => (long?)y.Id)
            .FirstOrDefaultAsync(ct);
        // Cast long? để query rỗng trả null (FirstOrDefault trên IQueryable<long> trả 0)
        var teamId = req.TeamId ?? await db.TeamMembers
            .Where(t => t.UserId == userId)
            .Select(t => (long?)t.TeamId)
            .FirstOrDefaultAsync(ct);

        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 1 or > 30)
            errors["name"] = ["Tên lớp từ 1 đến 30 ký tự."];
        if (yearId is null)
            errors["schoolYearId"] = ["Chưa có năm học nào được đặt làm hiện tại."];
        if (req.SchoolYearId is { } sy && !await db.SchoolYears.AnyAsync(x => x.Id == sy, ct))
            errors["schoolYearId"] = ["Năm học không tồn tại."];
        if (req.GradeId is { } g && !await db.Grades.AnyAsync(x => x.Id == g, ct))
            errors["gradeId"] = ["Khối không tồn tại."];
        if (req.TeamId is { } t && !await db.Teams.AnyAsync(x => x.Id == t, ct))
            errors["teamId"] = ["Tổ không tồn tại."];
        if (teamId is { } tm && !viewer.IsAdmin && !viewer.TeamIds.Contains(tm))
            errors["teamId"] = ["Lớp phải thuộc một tổ của bạn."];
        if (yearId is not null && await db.Classes.AnyAsync(c => c.SchoolYearId == yearId && c.Name == name, ct))
            errors["name"] = [$"Tên lớp \"{name}\" đã tồn tại trong năm học này."];

        if (errors.Count > 0)
            throw new ClassValidationException(errors);

        var cls = new ClassEntity
        {
            Name = name,
            GradeId = req.GradeId,
            SchoolYearId = yearId,
            TeamId = teamId,
            HomeroomTeacherId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var (tid, sid) in NormalizeTeachers(req.Teachers))
            cls.Teachers.Add(new ClassTeacher { Class = cls, UserId = tid, SubjectId = sid == 0 ? null : sid });

        db.Classes.Add(cls);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("class.create", "class", cls.Id.ToString(), new { name, yearId }, ct);
        var created = await GetAsync(cls.Id, ct);
        return ToDto(created!, 0, 0);
    }

    public async Task<ClassDto?> UpdateAsync(ViewerContext viewer, long classId, UpdateClassRequest req, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;

        if (req.Name is { Length: > 0 } nm)
            cls.Name = nm.Trim();
        if (req.GradeId.HasValue)
            cls.GradeId = req.GradeId.Value;
        if (req.SchoolYearId is { } yid)
            cls.SchoolYearId = yid;
        if (req.TeamId is not null)
            cls.TeamId = req.TeamId.Value;

        var errors = new Dictionary<string, string[]>();
        if (cls.Name.Length is < 1 or > 30)
            errors["name"] = ["Tên lớp từ 1 đến 30 ký tự."];
        if (cls.SchoolYearId is { } y && await db.Classes.AnyAsync(c => c.Id != classId
                && c.SchoolYearId == y && c.Name == cls.Name, ct))
            errors["name"] = ["Tên lớp đã tồn tại trong năm học này."];
        if (req.GradeId is { } g && !await db.Grades.AnyAsync(x => x.Id == g, ct))
            errors["gradeId"] = ["Khối không tồn tại."];
        if (req.TeamId is { } t && !await db.Teams.AnyAsync(x => x.Id == t, ct))
            errors["teamId"] = ["Tổ không tồn tại."];
        if (errors.Count > 0)
            throw new ClassValidationException(errors);

        if (req.Teachers is not null)
        {
            var want = NormalizeTeachers(req.Teachers).ToHashSet();
            foreach (var cur in cls.Teachers.ToList())
                if (!want.Contains((cur.UserId, cur.SubjectId ?? 0)))
                    db.ClassTeachers.Remove(cur);
            foreach (var (tid, sid) in want)
            {
                var have = cls.Teachers.Any(x => x.UserId == tid && (x.SubjectId ?? 0) == sid);
                if (!have)
                    cls.Teachers.Add(new ClassTeacher { Class = cls, UserId = tid, SubjectId = sid == 0 ? null : sid });
            }
        }

        cls.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var updated = await GetAsync(classId, ct);
        return updated is null ? null : ToDto(updated, 0, 0);
    }

    /// <summary>§7: xóa lớp = xóa cứng kèm bài đã giao, học sinh, lượt làm & câu trả lời liên quan.</summary>
    public async Task<bool> DeleteAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return false;

        var studentIds = await db.Students.Where(s => s.ClassId == classId).Select(s => s.Id).ToListAsync(ct);
        var assignmentIds = await db.Assignments.Where(a => a.ClassId == classId).Select(a => a.Id).ToListAsync(ct);
        var doomedAttempts = await db.Attempts
            .Where(a => studentIds.Contains(a.StudentId ?? 0) || assignmentIds.Contains(a.AssignmentId ?? 0))
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (doomedAttempts.Count > 0)
            await db.AttemptAnswers.Where(x => doomedAttempts.Contains(x.AttemptId)).ExecuteDeleteAsync(ct);
        if (doomedAttempts.Count > 0)
            await db.Attempts.Where(a => doomedAttempts.Contains(a.Id)).ExecuteDeleteAsync(ct);
        await db.ClassTeachers.Where(t => t.ClassId == classId).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.ClassId == classId).ExecuteDeleteAsync(ct);
        await db.Students.Where(s => s.ClassId == classId).ExecuteDeleteAsync(ct);
        await db.Classes.Where(c => c.Id == classId).ExecuteDeleteAsync(ct);

        await audit.LogAsync("class.delete", "class", classId.ToString(),
            new { name = cls.Name, students = studentIds.Count, attempts = doomedAttempts.Count }, ct);
        return true;
    }

    // ===== Học sinh =====

    public async Task<IReadOnlyList<StudentDto>> ListStudentsAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return [];
        var list = await db.Students.AsNoTracking()
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.Ordinal ?? 0)
            .ThenBy(s => s.FullName)
            .ToListAsync(ct);
        return list.Select(ToStudentDto).ToList();
    }

    public async Task<StudentDto> CreateStudentAsync(
        ViewerContext viewer, long classId, CreateStudentRequest req, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            throw new ClassNotFoundException();

        var name = req.FullName.Trim();
        if (name.Length is < 1 or > 100)
            throw new ClassValidationException(new() { ["fullName"] = ["Họ tên từ 1 đến 100 ký tự."] });
        if (req.StudentCode is { Length: > 40 })
            throw new ClassValidationException(new() { ["studentCode"] = ["Mã HS tối đa 40 ký tự."] });

        var nextOrdinal = await db.Students.Where(s => s.ClassId == classId)
            .Select(s => (int?)s.Ordinal)
            .MaxAsync(ct);

        var now = time.GetUtcNow();
        var student = new Student
        {
            ClassId = classId,
            Ordinal = (short)((nextOrdinal ?? 0) + 1),
            FullName = name,
            StudentCode = TrimToNull(req.StudentCode),
            DateOfBirth = req.DateOfBirth,
            Gender = TrimToNull(req.Gender),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return ToStudentDto(student);
    }

    public async Task<StudentDto?> UpdateStudentAsync(
        ViewerContext viewer, long classId, long studentId, UpdateStudentRequest req, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId, ct);
        if (student is null)
            return null;

        if (req.FullName is { } nm)
        {
            if (nm.Trim().Length is < 1 or > 100)
                throw new ClassValidationException(new() { ["fullName"] = ["Họ tên từ 1 đến 100 ký tự."] });
            student.FullName = nm.Trim();
        }
        if (req.StudentCode is not null)
            student.StudentCode = TrimToNull(req.StudentCode);
        if (req.DateOfBirth.HasValue)
            student.DateOfBirth = req.DateOfBirth;
        if (req.Gender is not null)
            student.Gender = TrimToNull(req.Gender);
        if (req.Ordinal is { } o and > 0)
            student.Ordinal = o;
        if (req.IsActive is { } a)
            student.IsActive = a;
        student.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToStudentDto(student);
    }

    /// <summary>§7: xóa học sinh → xóa cứng kèm lượt làm liên quan.</summary>
    public async Task<bool> DeleteStudentAsync(ViewerContext viewer, long classId, long studentId, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return false;
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId, ct);
        if (student is null)
            return false;

        var attemptIds = await db.Attempts
            .Where(a => a.StudentId == studentId)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (attemptIds.Count > 0)
            await db.AttemptAnswers.Where(x => attemptIds.Contains(x.AttemptId)).ExecuteDeleteAsync(ct);
        if (attemptIds.Count > 0)
            await db.Attempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync(ct);

        db.Students.Remove(student);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("student.delete", "student", studentId.ToString(),
            new { classId, attempts = attemptIds.Count }, ct);
        return true;
    }

    // ===== Nhập học sinh từ Excel =====

    /// <summary>
    /// §7: cột STT · Họ và tên* · Ngày sinh (dd/MM/yyyy) · Giới tính · Mã HS (sheet đầu).
    /// Trùng (họ tên + ngày sinh) với học sinh đã có trong lớp hoặc với dòng khác trong file → bỏ qua và báo dòng.
    /// </summary>
    public async Task<StudentImportResult> ImportStudentsAsync(
        ViewerContext viewer, long classId, Stream file, bool dryRun, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            throw new ClassNotFoundException();

        using var wb = new XLWorkbook(file);
        var ws = wb.Worksheet(1);
        if (ws is null || ws.Row(1).IsEmpty())
            throw new ClassValidationException(new() { ["file"] = ["File thiếu tiêu đề dòng đầu."] });

        var headers = ws.Row(1)
            .CellsUsed()
            .ToDictionary(c => NormalizeHeader(c.GetString()), c => c.Address.ColumnNumber);
        if (!headers.ContainsKey("ho va ten"))
            throw new ClassValidationException(new() { ["file"] = ["File phải có cột \"Họ và tên\"."] });

        int Col(string h) => headers.TryGetValue(h, out var n) ? n : 0;
        var cName = Col("ho va ten");
        var cDob = Col("ngay sinh");
        var cGender = Col("gioi tinh");
        var cCode = Col("ma hs");

        var existing = await db.Students.AsNoTracking()
            .Where(s => s.ClassId == classId)
            .Select(s => new { s.FullName, s.DateOfBirth })
            .ToListAsync(ct);
        var existingKeys = existing.Select(s => Key(s.FullName, s.DateOfBirth)).ToHashSet();

        var errors = new List<StudentImportRowError>();
        var duplicateCount = 0;
        var rows = new List<(short Ord, string Name, string? Code, DateOnly? Dob, string? Gender)>();
        var maxOrdinal = await db.Students.Where(s => s.ClassId == classId)
            .Select(s => (int?)s.Ordinal)
            .MaxAsync(ct) ?? 0;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            string Cell(int c) => c > 0 ? ws.Cell(r, c).GetFormattedString().Trim() : "";
            var name = Cell(cName);
            if (name.Length == 0 && Cell(cDob).Length == 0 && Cell(cCode).Length == 0)
                continue; // dòng trống

            if (name.Length == 0)
            {
                errors.Add(new StudentImportRowError(r, "Thiếu họ tên."));
                continue;
            }
            if (name.Length > 100)
            {
                errors.Add(new StudentImportRowError(r, "Họ tên quá 100 ký tự."));
                continue;
            }
            DateOnly? dob = null;
            var dobText = Cell(cDob);
            if (dobText.Length > 0 && !TryParseDob(dobText, out dob))
            {
                errors.Add(new StudentImportRowError(r, $"Ngày sinh \"{dobText}\" không đúng định dạng dd/MM/yyyy."));
                continue;
            }
            var code = Cell(cCode);
            if (code.Length > 40)
            {
                errors.Add(new StudentImportRowError(r, "Mã HS quá 40 ký tự."));
                continue;
            }

            var key = Key(name, dob);
            if (existingKeys.Contains(key) || rows.Any(x => Key(x.Name, x.Dob) == key))
            {
                duplicateCount++;
                errors.Add(new StudentImportRowError(r, $"Trùng với học sinh \"{name}\" đã có — bỏ qua."));
                continue;
            }

            maxOrdinal++;
            rows.Add(((short)maxOrdinal, name, code.Length == 0 ? null : code, dob,
                Cell(cGender).Length == 0 ? null : Cell(cGender)));
        }

        if (!dryRun && rows.Count > 0)
        {
            var now = time.GetUtcNow();
            foreach (var (ord, name, code, dob, gender) in rows)
                db.Students.Add(new Student
                {
                    ClassId = classId,
                    Ordinal = ord,
                    FullName = name,
                    StudentCode = code,
                    DateOfBirth = dob,
                    Gender = gender,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("students.import", "class", classId.ToString(),
                new { imported = rows.Count, errors = errors.Count }, ct);
        }

        return new StudentImportResult(dryRun, rows.Count + errors.Count, rows.Count, duplicateCount, errors);
    }

    public async Task<byte[]> ExportStudentsAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            throw new ClassNotFoundException();

        var students = await db.Students.AsNoTracking()
            .Where(s => s.ClassId == classId && s.IsActive)
            .OrderBy(s => s.Ordinal ?? 0)
            .ThenBy(s => s.FullName)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh sach HS");
        string[] headers = { "STT", "Họ và tên", "Ngày sinh", "Giới tính", "Mã HS" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        var r = 2;
        foreach (var s in students)
        {
            ws.Cell(r, 1).Value = s.Ordinal ?? (r - 1);
            ws.Cell(r, 2).Value = s.FullName;
            if (s.DateOfBirth is { } dob)
                ws.Cell(r, 3).Value = dob.ToString("dd/MM/yyyy");
            ws.Cell(r, 4).Value = s.Gender ?? "";
            ws.Cell(r, 5).Value = s.StudentCode ?? "";
            r++;
        }
        ws.Column(2).Width = 32;
        ws.Column(3).Width = 14;
        ws.Column(4).Width = 12;
        ws.Column(5).Width = 12;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        await audit.LogAsync("students.export", "class", classId.ToString(), new { count = students.Count }, ct);
        return ms.ToArray();
    }

    // ===== Bảng điểm =====

    /// <summary>§7: hàng = HS active, cột = bài đã giao của lớp (theo assignment), ô = điểm cao nhất (null = chưa làm).</summary>
    public async Task<GradebookDto?> GetGradebookAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;

        var students = await db.Students.AsNoTracking()
            .Where(s => s.ClassId == classId && s.IsActive)
            .OrderBy(s => s.Ordinal ?? 0)
            .ThenBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName })
            .ToListAsync(ct);

        var assignments = await db.Assignments.AsNoTracking()
            .Include(a => a.Quiz)
            .Where(a => a.ClassId == classId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.StudentId != null
                        && a.AssignmentId != null
                        && assignmentIds.Contains(a.AssignmentId ?? 0))
            .ToListAsync(ct);

        var byStudent = attempts
            .GroupBy(a => a.StudentId!.Value)
            .ToDictionary(g => g.Key, g => g
                .GroupBy(a => a.AssignmentId!.Value)
                .ToDictionary(k => k.Key, v => v.Max(x => x.Score10 ?? 0m)));

        var scores = students
            .Select(s => assignments
                .Select(a =>
                {
                    decimal? score = null;
                    if (byStudent.TryGetValue(s.Id, out var byA) && byA.TryGetValue(a.Id, out var sc))
                        score = sc;
                    return score;
                })
                .ToList())
            .ToList();

        return new GradebookDto(
            cls.Name,
            assignments
                .Select(a => new GradebookAssignmentColumn(a.Id, a.Quiz?.Title ?? "", a.Code, a.CreatedAt))
                .ToList(),
            students.Select(s => new GradebookStudentRow(s.Id, s.FullName)).ToList(),
            scores);
    }

    public async Task<byte[]> ExportGradebookAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var gb = await GetGradebookAsync(viewer, classId, ct);
        if (gb is null)
            throw new ClassNotFoundException();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Bang diem");
        ws.Cell(1, 1).Value = gb.ClassName;
        ws.Cell(1, 1).Style.Font.SetBold();
        ws.Cell(2, 1).Value = "STT";
        ws.Cell(2, 2).Value = "Họ và tên";
        for (var c = 0; c < gb.Columns.Count; c++)
            ws.Cell(2, 3 + c).Value = $"{gb.Columns[c].QuizTitle} ({gb.Columns[c].Code})";
        for (var r = 0; r < gb.Students.Count; r++)
        {
            ws.Cell(3 + r, 1).Value = r + 1;
            ws.Cell(3 + r, 2).Value = gb.Students[r].FullName;
            for (var c = 0; c < gb.Columns.Count; c++)
            {
                var score = gb.Scores[r][c];
                if (score is { } sc)
                    ws.Cell(3 + r, 3 + c).Value = sc;
            }
        }
        ws.Column(2).Width = 32;
        for (var c = 3; c <= 2 + gb.Columns.Count; c++)
            ws.Column(c).Width = 18;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        await audit.LogAsync("gradebook.export", "class", classId.ToString(),
            new { students = gb.Students.Count, assignments = gb.Columns.Count }, ct);
        return ms.ToArray();
    }

    // ===== Nội bộ =====

    /// <summary>(userId, subjectId ?? 0) — bỏ lặp, bỏ id không hợp lệ.</summary>
    private static List<(long UserId, long SubjectId)> NormalizeTeachers(IReadOnlyList<ClassTeacherItem>? items)
        => (items ?? [])
            .Where(i => i.UserId > 0)
            .Select(i => (i.UserId, (long)(i.SubjectId ?? 0)))
            .Distinct()
            .ToList();

    private static ClassDto ToDto(ClassEntity c, int studentCount, int assignmentCount) => new(
        c.Id,
        c.Name,
        c.GradeId,
        c.Grade?.Name,
        c.SchoolYearId,
        c.SchoolYear?.Name,
        c.TeamId,
        c.Team?.Name,
        c.HomeroomTeacherId,
        c.HomeroomTeacher?.FullName,
        c.Teachers
            .Select(t => new ClassTeacherDto(t.UserId, t.User?.FullName ?? "", t.SubjectId, t.Subject?.Name))
            .ToList(),
        studentCount,
        assignmentCount,
        c.IsArchived,
        c.CreatedAt,
        c.UpdatedAt);

    private static StudentDto ToStudentDto(Student s) => new(
        s.Id, s.Ordinal, s.FullName, s.StudentCode, s.DateOfBirth, s.Gender, s.IsActive, s.CreatedAt, s.UpdatedAt);

    private static string? TrimToNull(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>Bỏ dấu + hoa thường cho tiêu đề cột: "Họ và Tên" → "ho va ten".</summary>
    private static string NormalizeHeader(string s)
    {
        var nfkc = s.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var noMarks = new string(nfkc.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return noMarks.ToLowerInvariant();
    }

    private static bool TryParseDob(string text, out DateOnly? value)
    {
        foreach (var f in DobFormats)
        {
            if (DateTime.TryParseExact(text, f, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                value = DateOnly.FromDateTime(dt);
                return true;
            }
        }
        value = null;
        return false;
    }

    private static string Key(string name, DateOnly? dob)
        => name.Trim().ToLowerInvariant() + "|" + (dob?.ToString("yyyyMMdd") ?? "-");
}
