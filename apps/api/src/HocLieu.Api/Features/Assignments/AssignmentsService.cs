using System.Security.Cryptography;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Attempts;
using HocLieu.Features.Classes;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Assignments;

public sealed class AssignmentValidationException(Dictionary<string, string[]> errors) : Exception("Dữ liệu không hợp lệ")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Assignment chưa tới giờ mở (409 assignment.not_open).</summary>
public sealed class AssignmentNotOpenException : Exception;

/// <summary>Assignment đã qua giờ đóng (409 assignment.closed).</summary>
public sealed class AssignmentClosedException : Exception;

/// <summary>
/// §7: giao bài — quiz (của mình hoặc Public) + lớp + mã 6 ký tự + giờ mở/đóng.
/// Mã bảng chữ không 0/O/1/I/L (đọc không nhầm trên màn/Zalo).
/// Xóa assignment = xóa cứng kèm lượt làm (nhất quán với xóa lớp/học sinh).
/// </summary>
public class AssignmentsService(AppDbContext db, TimeProvider time, IAuditLogger audit, AttemptsService attempts)
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // ===== Quyền (chung với lớp) =====

    private async Task<ClassEntity?> GetClassCheckedAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls is null)
            return null;
        IReadOnlySet<long> teacherClassIds = new HashSet<long>();
        if (viewer.UserId is { } uid)
        {
            var ids = await db.ClassTeachers.AsNoTracking()
                .Where(t => t.UserId == uid)
                .Select(t => t.ClassId)
                .ToListAsync(ct);
            teacherClassIds = ids.ToHashSet();
        }
        if (!ClassesService.CanAccessClass(viewer, cls, teacherClassIds))
            throw new ClassForbiddenException();
        return cls;
    }

    // ===== CRUD (GV) =====

    public async Task<AssignmentDto?> CreateAsync(ViewerContext viewer, long classId, CreateAssignmentRequest req, CancellationToken ct)
    {
        if (viewer.UserId is not { } userId)
            return null;
        var cls = await GetClassCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;

        var errors = new Dictionary<string, string[]>();
        var quiz = await db.Quizzes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == req.QuizId && !q.IsDeleted, ct);
        if (quiz is null || (quiz.OwnerId != userId && quiz.Scope != ContentScope.Public))
            errors["quizId"] = ["Chỉ giao được bài tập của mình hoặc bài tập công khai."];
        if (req.OpenAt is { } open && req.CloseAt is { } close && close <= open)
            errors["closeAt"] = ["Giờ đóng phải sau giờ mở."];
        if (errors.Count > 0)
            throw new AssignmentValidationException(errors);
        // quiz null hoặc sai quyền ⟹ đã có lỗi và throw ở trên; đến đây quiz chắc chắn hợp lệ.
        var q = quiz!;

        var assignment = new Assignment
        {
            QuizId = q.Id,
            ClassId = classId,
            Code = await NewUniqueCodeAsync(ct),
            UseRoster = req.UseRoster,
            OpenAt = req.OpenAt,
            CloseAt = req.CloseAt,
            CreatedBy = userId,
            CreatedAt = time.GetUtcNow(),
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("assignment.create", "Assignment", assignment.Id.ToString(),
            new { quizId = q.Id, classId, assignment.Code, useRoster = req.UseRoster }, ct);
        return await GetDtoAsync(viewer, classId, assignment.Id, ct);
    }

    public async Task<AssignmentDto?> GetDtoAsync(ViewerContext viewer, long classId, long assignmentId, CancellationToken ct)
    {
        var cls = await GetClassCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;
        var a = await db.Assignments.AsNoTracking()
            .Include(x => x.Quiz)
            .Include(x => x.Creator)
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.ClassId == classId, ct);
        if (a is null || a.Quiz is null)
            return null;
        return await ToDtoAsync(a, ct);
    }

    public async Task<IReadOnlyList<AssignmentClassRowDto>> ListForClassAsync(ViewerContext viewer, long classId, CancellationToken ct)
    {
        var cls = await GetClassCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return [];

        var assignments = await db.Assignments.AsNoTracking()
            .Include(a => a.Quiz)
            .Where(a => a.ClassId == classId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var ids = assignments.Select(a => a.Id).ToList();
        var stats = await db.Attempts.AsNoTracking()
            .Where(a => a.AssignmentId != null && ids.Contains(a.AssignmentId.Value))
            .Select(a => new { a.AssignmentId, a.Score10 })
            .ToListAsync(ct);
        var byAssignment = stats
            .Where(s => s.AssignmentId is not null)
            .GroupBy(s => s.AssignmentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return assignments
            .Select(a =>
            {
                byAssignment.TryGetValue(a.Id, out var list);
                decimal? best = null;
                if (list is not null)
                {
                    foreach (var s in list)
                        if (s.Score10 is { } v && (best is null || v > best.Value))
                            best = v;
                }
                return new AssignmentClassRowDto(
                    a.Id, a.Quiz?.Title ?? "", a.Code, a.UseRoster, a.OpenAt, a.CloseAt,
                    list?.Count ?? 0, best, a.CreatedAt);
            })
            .ToList();
    }

    /// <summary>Chỉ đổi giờ mở/đóng & use_roster (null = giữ giá trị cũ; không hỗ trợ xóa giờ).</summary>
    public async Task<AssignmentDto?> UpdateAsync(ViewerContext viewer, long classId, long assignmentId, UpdateAssignmentRequest req, CancellationToken ct)
    {
        var cls = await GetClassCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return null;
        var a = await db.Assignments
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.ClassId == classId, ct);
        if (a is null)
            return null;

        var open = req.OpenAt ?? a.OpenAt;
        var close = req.CloseAt ?? a.CloseAt;
        if (open is { } o && close is { } c && c <= o)
            throw new AssignmentValidationException(new() { ["closeAt"] = ["Giờ đóng phải sau giờ mở."] });

        a.UseRoster = req.UseRoster ?? a.UseRoster;
        a.OpenAt = open;
        a.CloseAt = close;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("assignment.update", "Assignment", a.Id.ToString(),
            new { useRoster = a.UseRoster, a.OpenAt, a.CloseAt }, ct);
        return await ToDtoAsync(a, ct);
    }

    /// <summary>Xóa assignment + XÓA CỨNG các lượt làm của nó (kèm câu trả lời).</summary>
    public async Task<bool> DeleteAsync(ViewerContext viewer, long classId, long assignmentId, CancellationToken ct)
    {
        var cls = await GetClassCheckedAsync(viewer, classId, ct);
        if (cls is null)
            return false;
        var a = await db.Assignments
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.ClassId == classId, ct);
        if (a is null)
            return false;

        var attemptIds = await db.Attempts
            .Where(x => x.AssignmentId == a.Id)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (attemptIds.Count > 0)
        {
            await db.AttemptAnswers
                .Where(x => attemptIds.Contains(x.AttemptId))
                .ExecuteDeleteAsync(ct);
            await db.Attempts
                .Where(x => attemptIds.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
        }
        db.Assignments.Remove(a);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("assignment.delete", "Assignment", a.Id.ToString(),
            new { a.Code, attemptCount = attemptIds.Count }, ct);
        return true;
    }

    // ===== Công khai: mã giao bài =====

    /// <summary>
    /// §7: thông tin bài giao theo mã. Roster (id + fullName) chỉ trả khi use_roster
    /// VÀ đang mở — không lộ danh sách lớp khi chưa mở/đã đóng.
    /// UsedAttempts tính theo thiết bị (cookie) để FE hiện "số lượt đã dùng".
    /// </summary>
    public async Task<PublicAssignmentDto?> GetPublicAsync(string code, string? deviceId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var a = await db.Assignments.AsNoTracking()
            .Include(x => x.Quiz)
            .Include(x => x.Class)
            .FirstOrDefaultAsync(x => x.Code == code, ct);
        if (a is null || a.Quiz is null)
            return null;

        var status = a.OpenAt is { } open && now < open ? "Scheduled"
            : a.CloseAt is { } close && now >= close ? "Closed"
            : "Open";
        var isOpen = status == "Open";

        IReadOnlyList<PublicRosterStudentDto>? roster = null;
        if (isOpen && a.UseRoster)
        {
            roster = await db.Students.AsNoTracking()
                .Where(s => s.ClassId == a.ClassId && s.IsActive)
                .OrderBy(s => s.Ordinal ?? 0)
                .ThenBy(s => s.FullName)
                .Select(s => new PublicRosterStudentDto(s.Id, s.FullName))
                .ToListAsync(ct);
        }

        var used = deviceId is { } dev
            ? await db.Attempts.AsNoTracking().CountAsync(x => x.QuizId == a.QuizId && x.DeviceId == dev, ct)
            : await db.Attempts.AsNoTracking().CountAsync(x => x.QuizId == a.QuizId, ct);

        return new PublicAssignmentDto(
            a.Code, status, a.Quiz.Title, a.Quiz.QuestionCount, a.Quiz.TimeLimitMinutes,
            a.Quiz.IdentityMode.ToString(), a.Quiz.MaxAttempts, used,
            a.OpenAt, a.CloseAt, a.Class?.Name ?? "", roster);
    }

    /// <summary>
    /// §7: tạo attempt qua mã giao bài — KHÔNG phụ thuộc trạng thái ẩn/hiện của quiz
    /// (GV có thể giao bài đang ẩn chỉ cho lớp mình); chỉ cần assignment đang mở.
    /// null = mã không tồn tại (404); ném NotOpen/Closed (409) theo trạng thái.
    /// </summary>
    public async Task<AttemptDto?> CreateAttemptAsync(
        string code, CreateAssignmentAttemptRequest? req, string deviceId, string? ipHash, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var a = await db.Assignments
            .Include(x => x.Quiz)
            .FirstOrDefaultAsync(x => x.Code == code, ct);
        if (a is null || a.Quiz is null)
            return null;
        if (a.OpenAt is { } open && now < open)
            throw new AssignmentNotOpenException();
        if (a.CloseAt is { } close && now >= close)
            throw new AssignmentClosedException();

        var quiz = a.Quiz;
        long? studentId = null;
        string? guestName = req?.GuestName?.Trim();
        string? guestClass = null;

        if (a.UseRoster)
        {
            if (req?.StudentId is not { } sid)
                throw new AttemptValidationException(new() { ["studentId"] = ["Vui lòng chọn tên trong danh sách lớp."] });
            var student = await db.Students.AsNoTracking()
                .Include(s => s.Class)
                .FirstOrDefaultAsync(s => s.Id == sid && s.ClassId == a.ClassId && s.IsActive, ct);
            if (student is null)
                throw new AttemptValidationException(new() { ["studentId"] = ["Tên này không có trong danh sách lớp."] });
            studentId = student.Id;
            guestName = student.FullName;
            guestClass = student.Class?.Name;
        }
        else if (quiz.IdentityMode is IdentityMode.Name or IdentityMode.NameAndClass)
        {
            if (string.IsNullOrWhiteSpace(guestName))
                throw new AttemptValidationException(new() { ["guestName"] = ["Vui lòng nhập họ tên."] });
            if (guestName.Length > 100)
                throw new AttemptValidationException(new() { ["guestName"] = ["Họ tên tối đa 100 ký tự."] });
            if (guestClass is { Length: > 60 })
                throw new AttemptValidationException(new() { ["guestClass"] = ["Lớp tối đa 60 ký tự."] });
        }

        return await attempts.CreateForAssignmentAsync(quiz, a.Id, studentId, guestName, guestClass, deviceId, ipHash, ct);
    }

    // ===== Nội bộ =====

    private async Task<AssignmentDto> ToDtoAsync(Assignment a, CancellationToken ct)
    {
        var stats = await db.Attempts.AsNoTracking()
            .Where(x => x.AssignmentId == a.Id)
            .Select(x => new { x.Score10 })
            .ToListAsync(ct);
        decimal? best = null;
        foreach (var s in stats)
            if (s.Score10 is { } v && (best is null || v > best.Value))
                best = v;
        return new AssignmentDto(
            a.Id, a.QuizId, a.Quiz?.Title ?? "", a.Code, a.UseRoster, a.OpenAt, a.CloseAt,
            stats.Count, best, a.Creator?.FullName ?? "", a.CreatedAt);
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        var bytes = new byte[6];
        for (var i = 0; i < 10; i++)
        {
            RandomNumberGenerator.Fill(bytes);
            var code = new string(bytes.Select(b => CodeAlphabet[b % CodeAlphabet.Length]).ToArray());
            var exists = await db.Assignments.AnyAsync(x => x.Code == code, ct);
            if (!exists)
                return code;
        }
        throw new InvalidOperationException("Không tạo được mã giao bài duy nhất.");
    }
}
