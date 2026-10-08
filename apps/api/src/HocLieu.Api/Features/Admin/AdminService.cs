using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

public sealed class AdminFlowException(int status, string code, string title) : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
}

public sealed record RolloverResult(long OldYearId, long NewYearId, int ArchivedClasses, int ClonedClasses, int ClonedStudents);

/// <summary>
/// §5.4 (M6): logic admin dùng chung — sửa người dùng (guard "không khóa/hạ quyền Admin cuối
/// cùng đang Active"), chuyển quyền sở hữu nội dung, kết chuyển năm học (spec §16 M6).
/// </summary>
public sealed class AdminService(AppDbContext db, SessionStampService stamps, TimeProvider time)
{
    /// <summary>
    /// Patch người dùng. Giá trị null = không đổi.
    /// Guard (spec §2.3): Admin cuối cùng đang Active không thể bị đổi trạng thái khỏi Active
    /// hoặc hạ về Teacher → 409.
    /// Đổi trạng thái / vai trò / tổ → đổi security_stamp (thu hồi mọi phiên, spec §3.4).
    /// </summary>
    public async Task<User> ApplyUserPatchAsync(
        long userId, string? status, string? statusReason, string? systemRole, long[]? teamIds, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AdminFlowException(404, "not_found", "Không tìm thấy người dùng.");

        UserStatus? newStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<UserStatus>(status, true, out var st))
                throw new AdminFlowException(422, "validation", "Trạng thái không hợp lệ.");
            newStatus = st;
        }
        SystemRole? newRole = null;
        if (systemRole is not null)
        {
            if (!Enum.TryParse<SystemRole>(systemRole, true, out var sr))
                throw new AdminFlowException(422, "validation", "Vai trò không hợp lệ.");
            newRole = sr;
        }

        if (user.SystemRole == SystemRole.Admin && user.Status == UserStatus.Active)
        {
            var activeAdmins = await db.Users
                .CountAsync(u => u.SystemRole == SystemRole.Admin && u.Status == UserStatus.Active, ct);
            if (activeAdmins == 1
                && ((newStatus is not null && newStatus != UserStatus.Active)
                    || (newRole is not null && newRole != SystemRole.Admin)))
                throw new AdminFlowException(409, "last_admin",
                    "Không thể khóa hoặc hạ quyền Admin cuối cùng đang hoạt động.");
        }

        var statusChange = newStatus is not null && newStatus != user.Status;
        var roleChange = newRole is not null && newRole != user.SystemRole;
        var teamChange = teamIds is not null;

        if (statusChange)
        {
            user.Status = newStatus!.Value;
            // Khóa/từ chối → lưu lý do; mở lại (Active/Pending) → xóa lý do cũ
            user.StatusReason = newStatus is UserStatus.Suspended or UserStatus.Rejected ? statusReason : null;
        }
        else if (newStatus is not null && user.Status is UserStatus.Suspended or UserStatus.Rejected)
            user.StatusReason = statusReason;

        if (roleChange)
            user.SystemRole = newRole!.Value;

        var stampChanged = statusChange || roleChange || teamChange;

        if (teamChange)
        {
            var wanted = new HashSet<long>(teamIds!);
            var current = user.TeamMemberships.ToList();
            foreach (var m in current.Where(m => !wanted.Contains(m.TeamId)).ToList())
                db.TeamMembers.Remove(m);
            var missing = wanted.Except(current.Select(m => m.TeamId)).ToList();
            if (missing.Count > 0
                && !await db.Teams.AnyAsync(t => t.IsActive && missing.Contains(t.Id), ct))
                throw new AdminFlowException(422, "validation", "Có tổ không tồn tại hoặc đã ngừng hoạt động.");
            foreach (var teamId in missing)
                db.TeamMembers.Add(new TeamMember
                {
                    TeamId = teamId,
                    UserId = user.Id,
                    Role = TeamRole.Member,
                    JoinedAt = time.GetUtcNow(),
                });
        }

        if (stampChanged)
            user.SecurityStamp = Guid.NewGuid();

        await db.SaveChangesAsync(ct);
        if (stampChanged)
            await stamps.InvalidateUserAsync(user.Id, ct);
        return user;
    }

    /// <summary>
    /// §5.4: chuyển toàn bộ tài liệu & bài tập (kể cả đã xóa mềm) của user này sang user khác.
    /// </summary>
    public async Task<(int Documents, int Quizzes)> TransferContentAsync(long fromUserId, long toUserId, CancellationToken ct)
    {
        if (fromUserId == toUserId)
            throw new AdminFlowException(422, "validation", "Không thể chuyển nội dung về chính người đó.");
        if (!await db.Users.AnyAsync(u => u.Id == toUserId, ct))
            throw new AdminFlowException(404, "not_found", "Không tìm thấy người nhận nội dung.");

        var now = time.GetUtcNow();
        var documents = await db.Documents.IgnoreQueryFilters()
            .Where(d => d.OwnerId == fromUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.OwnerId, toUserId)
                .SetProperty(d => d.UpdatedAt, now), ct);
        var quizzes = await db.Quizzes.IgnoreQueryFilters()
            .Where(q => q.OwnerId == fromUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.OwnerId, toUserId)
                .SetProperty(q => q.UpdatedAt, now), ct);
        return (documents, quizzes);
    }

    /// <summary>
    /// §5.4: kết chuyển năm học. <paramref name="newYearId"/> = năm mới (phải đã tồn tại,
    /// chưa phải năm hiện tại). Năm cũ = năm đang current: lưu trữ mọi lớp (IsArchived),
    /// tùy chọn nhân bản lớp lên khối +1 kèm học sinh + GV bộ môn (khối 5 chỉ lưu trữ).
    /// </summary>
    public async Task<RolloverResult> RolloverYearAsync(long newYearId, bool cloneClasses, CancellationToken ct)
    {
        var newYear = await db.SchoolYears.FirstOrDefaultAsync(y => y.Id == newYearId, ct)
            ?? throw new AdminFlowException(404, "not_found", "Không tìm thấy năm học.");
        if (newYear.IsCurrent)
            throw new AdminFlowException(422, "validation", "Năm này đã là năm học hiện tại.");

        var oldYear = await db.SchoolYears.FirstOrDefaultAsync(y => y.IsCurrent, ct)
            ?? throw new AdminFlowException(422, "validation", "Chưa có năm học hiện tại để kết chuyển.");

        // Hai bước: buông cờ trước, gán cờ sau — tránh vi phạm ux_one_current_year
        // nếu EF đặt câu UPDATE newYear trước oldYear trong cùng batch.
        oldYear.IsCurrent = false;
        await db.SaveChangesAsync(ct);
        newYear.IsCurrent = true;

        var oldClasses = await db.Classes
            .Where(c => c.SchoolYearId == oldYear.Id && !c.IsArchived)
            .Include(c => c.Students)
            .Include(c => c.Teachers)
            .ToListAsync(ct);

        var archived = 0;
        var cloned = 0;
        var clonedStudents = 0;
        foreach (var cls in oldClasses)
        {
            cls.IsArchived = true;
            archived++;

            if (!cloneClasses)
                continue;
            // "not (…)" phải có ngoặc: "is not > 0 and < 5" hiểu là "(not > 0) and < 5"
            if (cls.GradeId is not (> 0 and < 5))
                continue; // khối 5 (hoặc không có khối) chỉ lưu trữ

            var newClass = new ClassEntity
            {
                Name = cls.Name,
                GradeId = (short?)(cls.GradeId + 1), // đã guard khối 1-4 ở trên
                SchoolYearId = newYear.Id,
                TeamId = cls.TeamId,
                HomeroomTeacherId = cls.HomeroomTeacherId,
                IsArchived = false,
            };
            foreach (var s in cls.Students)
                newClass.Students.Add(new Student
                {
                    Ordinal = s.Ordinal,
                    FullName = s.FullName,
                    StudentCode = s.StudentCode,
                    DateOfBirth = s.DateOfBirth,
                    Gender = s.Gender,
                    IsActive = s.IsActive,
                });
            foreach (var t in cls.Teachers)
                newClass.Teachers.Add(new ClassTeacher
                {
                    UserId = t.UserId,
                    SubjectId = t.SubjectId,
                });
            db.Classes.Add(newClass);
            cloned++;
            clonedStudents += cls.Students.Count;
        }

        await db.SaveChangesAsync(ct);
        return new RolloverResult(oldYear.Id, newYear.Id, archived, cloned, clonedStudents);
    }
}
