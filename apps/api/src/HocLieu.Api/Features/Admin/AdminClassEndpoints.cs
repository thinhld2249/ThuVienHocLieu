using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/lop (M6): mọi lớp; đổi GV chủ nhiệm.</summary>
public static class AdminClassEndpoints
{
    public static IEndpointRouteBuilder MapAdminClassEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/classes", async (
                AppDbContext db,
                [FromQuery] long? schoolYearId, [FromQuery] short? gradeId, [FromQuery] long? teamId,
                [FromQuery] string? q, [FromQuery] bool? archived,
                [FromQuery] int? page, [FromQuery] int? pageSize,
                CancellationToken ct) =>
            {
                IQueryable<ClassEntity> query = db.Classes.AsNoTracking();
                if (schoolYearId is not null) query = query.Where(c => c.SchoolYearId == schoolYearId);
                if (gradeId is not null) query = query.Where(c => c.GradeId == gradeId);
                if (teamId is not null) query = query.Where(c => c.TeamId == teamId);
                if (archived is not null) query = query.Where(c => c.IsArchived == archived.Value);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var text = q.Trim();
                    query = query.Where(c => c.Name.Contains(text));
                }

                (var p, var ps) = Pagination.Parse(page, pageSize, 25);
                var total = await query.CountAsync(ct);
                var rows = await query
                    .OrderBy(c => c.SchoolYearId).ThenBy(c => c.Name)
                    .Skip((p - 1) * ps).Take(ps)
                    .Select(c => new ClassRow(
                        c.Id, c.Name,
                        c.GradeId, c.Grade == null ? null : c.Grade.Name,
                        c.SchoolYearId, c.SchoolYear == null ? null : c.SchoolYear.Name,
                        c.TeamId, c.Team == null ? null : c.Team.Name,
                        c.HomeroomTeacherId, c.HomeroomTeacher == null ? null : c.HomeroomTeacher.FullName,
                        c.Students.Count(s => s.IsActive),
                        c.IsArchived, c.CreatedAt))
                    .ToListAsync(ct);

                var items = rows
                    .Select(r => new AdminClassDto(r.Id, r.Name, r.GradeId, r.GradeName,
                        r.SchoolYearId, r.SchoolYearName, r.TeamId, r.TeamName,
                        r.HomeroomTeacherId, r.HomeroomTeacherName, r.ActiveStudentCount,
                        r.IsArchived, r.CreatedAt.ToString("o")))
                    .ToList();
                return Results.Ok(new PagedResult<AdminClassDto>(items, total, p, ps));
            })
            .WithName("admin.classes.list")
            .WithSummary("Mọi lớp (lọc năm học/khối/tổ/tình trạng lưu trữ, tìm theo tên)")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("schoolYearId", "integer"))
            .WithMetadata(new QueryParameter("gradeId", "integer"))
            .WithMetadata(new QueryParameter("teamId", "integer"))
            .WithMetadata(new QueryParameter("q"))
            .WithMetadata(new QueryParameter("archived", "boolean"))
            .WithMetadata(new QueryParameter("page", "integer"))
            .WithMetadata(new QueryParameter("pageSize", "integer"));

        app.MapPatch("/api/admin/classes/{id:long}", async (
                long id, ClassHomeroomRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req?.HomeroomTeacherId is null or <= 0)
                    return ApiErrors.Validation(new() { ["homeroomTeacherId"] = ["Thiếu giáo viên chủ nhiệm."] });
                var teacher = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.HomeroomTeacherId, ct);
                if (teacher is null)
                    return ApiErrors.NotFound("Không tìm thấy giáo viên chủ nhiệm.");
                if (teacher.Status != Domain.UserStatus.Active)
                    return ApiErrors.Validation(new() { ["homeroomTeacherId"] = ["Giáo viên này chưa có tài khoản đang hoạt động."] });

                var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == id, ct);
                if (cls is null)
                    return ApiErrors.NotFound("Không tìm thấy lớp.");
                cls.HomeroomTeacherId = teacher.Id;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.class.homeroom", "class", cls.Id.ToString(),
                    new { homeroomTeacherId = teacher.Id, homeroomTeacherName = teacher.FullName }, ct);
                return Results.NoContent(); // FE tải lại danh sách
            })
            .WithName("admin.classes.patch")
            .WithSummary("Đổi GV chủ nhiệm lớp")
            .RequireAuthorization("Admin");

        return app;
    }
}

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record ClassRow(
    long Id, string Name,
    short? GradeId, string? GradeName,
    long? SchoolYearId, string? SchoolYearName,
    long? TeamId, string? TeamName,
    long? HomeroomTeacherId, string? HomeroomTeacherName,
    int ActiveStudentCount, bool IsArchived, DateTimeOffset CreatedAt);

public record AdminClassDto(
    long Id, string Name,
    short? GradeId, string? GradeName,
    long? SchoolYearId, string? SchoolYearName,
    long? TeamId, string? TeamName,
    long? HomeroomTeacherId, string? HomeroomTeacherName,
    int ActiveStudentCount, bool IsArchived, string CreatedAt);

public record ClassHomeroomRequest(long? HomeroomTeacherId);
