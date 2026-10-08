using System.Net;
using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/danh-muc + /admin/nam-hoc (M6): danh mục (chuyên mục, khối, môn, năm học, tags) + kết chuyển năm học.</summary>
public static class AdminTaxonomyEndpoints
{
    public static IEndpointRouteBuilder MapAdminTaxonomyEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Chuyên mục =====
        app.MapGet("/api/admin/sections", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Sections.AsNoTracking()
                .OrderBy(s => s.Sort).ThenBy(s => s.Name)
                .Select(s => new SectionAdminDto(s.Id, s.Slug, s.Name, s.Icon, s.Color, s.Sort,
                    s.ContentKind.ToString(), s.DefaultPublishMode.ToString(), s.DefaultScope.ToString(),
                    s.RequireWeek, s.IsInternal, s.IsActive))
                .ToListAsync(ct)))
            .WithName("admin.sections.list")
            .WithSummary("Mọi chuyên mục (kể cả đã tắt)")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/sections", async (
                SectionRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên chuyên mục."] });
                var error = ValidateSection(req);
                if (error is not null)
                    return ApiErrors.Validation(new() { ["body"] = [error] });
                var slug = Slugify.ToSlug(req.Name.Trim());
                if (await db.Sections.AnyAsync(s => s.Slug == slug, ct))
                    return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, "Chuyên mục này đã tồn tại.");

                var section = new Section
                {
                    Slug = slug,
                    Name = req.Name.Trim(),
                    Icon = req.Icon,
                    Color = req.Color,
                    Sort = req.Sort ?? 100,
                    ContentKind = (SectionContentKind)Enum.Parse(typeof(SectionContentKind), req.ContentKind ?? "Document", true),
                    DefaultPublishMode = (PublishMode)Enum.Parse(typeof(PublishMode), req.DefaultPublishMode ?? "Visible", true),
                    DefaultScope = (ContentScope)Enum.Parse(typeof(ContentScope), req.DefaultScope ?? "Public", true),
                    RequireWeek = req.RequireWeek ?? false,
                    IsInternal = req.IsInternal ?? false,
                    IsActive = req.IsActive ?? true,
                };
                db.Sections.Add(section);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.section.create", "section", section.Id.ToString(), new { name = section.Name }, ct);
                return Results.Created($"/api/admin/sections/{section.Id}", new SectionAdminDto(
                    section.Id, section.Slug, section.Name, section.Icon, section.Color, section.Sort,
                    section.ContentKind.ToString(), section.DefaultPublishMode.ToString(), section.DefaultScope.ToString(),
                    section.RequireWeek, section.IsInternal, section.IsActive));
            })
            .WithName("admin.sections.create")
            .WithSummary("Tạo chuyên mục")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/sections/{id:long}", async (
                long id, SectionRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên chuyên mục."] });
                var error = ValidateSection(req);
                if (error is not null)
                    return ApiErrors.Validation(new() { ["body"] = [error] });

                var section = await db.Sections.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (section is null)
                    return ApiErrors.NotFound("Không tìm thấy chuyên mục.");
                section.Name = req.Name.Trim();
                section.Icon = req.Icon;
                section.Color = req.Color;
                section.Sort = req.Sort ?? section.Sort;
                if (req.ContentKind is not null)
                    section.ContentKind = (SectionContentKind)Enum.Parse(typeof(SectionContentKind), req.ContentKind, true);
                if (req.DefaultPublishMode is not null)
                    section.DefaultPublishMode = (PublishMode)Enum.Parse(typeof(PublishMode), req.DefaultPublishMode, true);
                if (req.DefaultScope is not null)
                    section.DefaultScope = (ContentScope)Enum.Parse(typeof(ContentScope), req.DefaultScope, true);
                if (req.RequireWeek is not null) section.RequireWeek = req.RequireWeek.Value;
                if (req.IsInternal is not null) section.IsInternal = req.IsInternal.Value;
                if (req.IsActive is not null) section.IsActive = req.IsActive.Value;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.section.update", "section", section.Id.ToString(), new { name = section.Name }, ct);
                return Results.Ok(new SectionAdminDto(
                    section.Id, section.Slug, section.Name, section.Icon, section.Color, section.Sort,
                    section.ContentKind.ToString(), section.DefaultPublishMode.ToString(), section.DefaultScope.ToString(),
                    section.RequireWeek, section.IsInternal, section.IsActive));
            })
            .WithName("admin.sections.update")
            .WithSummary("Sửa chuyên mục (bật/tắt, màu, thứ tự, mặc định)")
            .RequireAuthorization("Admin");

        app.MapDelete("/api/admin/sections/{id:long}", async (
                long id, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var section = await db.Sections.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (section is null)
                    return ApiErrors.NotFound("Không tìm thấy chuyên mục.");
                section.IsActive = false; // mềm: nội dung cũ giữ section_id hợp lệ
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.section.deactivate", "section", section.Id.ToString(), null, ct);
                return Results.NoContent();
            })
            .WithName("admin.sections.deactivate")
            .WithSummary("Tắt chuyên mục (không xóa cứng — nội dung cũ vẫn hợp lệ)")
            .RequireAuthorization("Admin");

        // ===== Khối =====
        app.MapGet("/api/admin/grades", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Grades.AsNoTracking()
                .OrderBy(g => g.Sort)
                .Select(g => new GradeAdminDto(g.Id, g.Name, g.Sort))
                .ToListAsync(ct)))
            .WithName("admin.grades.list")
            .WithSummary("Mọi khối lớp")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/grades", async (
                GradeRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên khối."] });
                if (await db.Grades.AnyAsync(g => g.Name == req.Name.Trim(), ct))
                    return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, "Khối này đã tồn tại.");
                short nextId = (short)(await db.Grades.MaxAsync(g => g.Id, ct) + 1);
                var grade = new Grade { Id = nextId, Name = req.Name.Trim(), Sort = req.Sort ?? nextId };
                db.Grades.Add(grade);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.grade.create", "grade", grade.Id.ToString(), new { name = grade.Name }, ct);
                return Results.Created($"/api/admin/grades/{grade.Id}", new GradeAdminDto(grade.Id, grade.Name, grade.Sort));
            })
            .WithName("admin.grades.create")
            .WithSummary("Thêm khối (vd: Khối 6 khi mở rộng)")
            .RequireAuthorization("Admin");

        // ":short" không phải route constraint built-in — dùng :int (binding về short vẫn chạy)
        app.MapPut("/api/admin/grades/{id:int}", async (
                short id, GradeRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên khối."] });
                var grade = await db.Grades.FirstOrDefaultAsync(g => g.Id == id, ct);
                if (grade is null)
                    return ApiErrors.NotFound("Không tìm thấy khối.");
                grade.Name = req.Name.Trim();
                grade.Sort = req.Sort ?? grade.Sort;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.grade.update", "grade", grade.Id.ToString(), new { name = grade.Name }, ct);
                return Results.Ok(new GradeAdminDto(grade.Id, grade.Name, grade.Sort));
            })
            .WithName("admin.grades.update")
            .WithSummary("Sửa tên/thứ tự khối")
            .RequireAuthorization("Admin");

        // ===== Môn học =====
        app.MapGet("/api/admin/subjects", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Subjects.AsNoTracking()
                .OrderBy(s => s.Sort).ThenBy(s => s.Name)
                .Select(s => new SubjectAdminDto(s.Id, s.Name, s.Slug, s.Sort, s.IsActive))
                .ToListAsync(ct)))
            .WithName("admin.subjects.list")
            .WithSummary("Mọi môn học")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/subjects", async (
                SubjectRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên môn học."] });
                var slug = Slugify.ToSlug(req.Name.Trim());
                if (await db.Subjects.AnyAsync(s => s.Slug == slug, ct))
                    return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, "Môn học này đã tồn tại.");
                var subject = new Subject
                {
                    Name = req.Name.Trim(),
                    Slug = slug,
                    Sort = req.Sort ?? 100,
                    IsActive = req.IsActive ?? true,
                };
                db.Subjects.Add(subject);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.subject.create", "subject", subject.Id.ToString(), new { name = subject.Name }, ct);
                return Results.Created($"/api/admin/subjects/{subject.Id}", new SubjectAdminDto(subject.Id, subject.Name, subject.Slug, subject.Sort, subject.IsActive));
            })
            .WithName("admin.subjects.create")
            .WithSummary("Thêm môn học")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/subjects/{id:long}", async (
                long id, SubjectRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên môn học."] });
                var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (subject is null)
                    return ApiErrors.NotFound("Không tìm thấy môn học.");
                subject.Name = req.Name.Trim();
                subject.Sort = req.Sort ?? subject.Sort;
                if (req.IsActive is not null) subject.IsActive = req.IsActive.Value;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.subject.update", "subject", subject.Id.ToString(), new { name = subject.Name, isActive = subject.IsActive }, ct);
                return Results.Ok(new SubjectAdminDto(subject.Id, subject.Name, subject.Slug, subject.Sort, subject.IsActive));
            })
            .WithName("admin.subjects.update")
            .WithSummary("Sửa môn học (bật/tắt, thứ tự)")
            .RequireAuthorization("Admin");

        app.MapDelete("/api/admin/subjects/{id:long}", async (
                long id, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (subject is null)
                    return ApiErrors.NotFound("Không tìm thấy môn học.");
                subject.IsActive = false;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.subject.deactivate", "subject", subject.Id.ToString(), null, ct);
                return Results.NoContent();
            })
            .WithName("admin.subjects.deactivate")
            .WithSummary("Tắt môn học (không xóa cứng)")
            .RequireAuthorization("Admin");

        // ===== Năm học =====
        app.MapGet("/api/admin/school-years", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.SchoolYears.AsNoTracking()
                .OrderByDescending(y => y.StartDate)
                .Select(y => new SchoolYearAdminDto(y.Id, y.Name, y.StartDate, y.EndDate, y.IsCurrent))
                .ToListAsync(ct)))
            .WithName("admin.school_years.list")
            .WithSummary("Mọi năm học")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/school-years", async (
                SchoolYearRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var error = ValidateSchoolYear(req);
                if (error is not null)
                    return ApiErrors.Validation(new() { ["body"] = [error] });
                if (await db.SchoolYears.AnyAsync(y => y.Name == req!.Name.Trim(), ct))
                    return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, "Năm học này đã tồn tại.");
                var year = new SchoolYear
                {
                    Name = req.Name.Trim(),
                    StartDate = req.StartDate,
                    EndDate = req.EndDate,
                    IsCurrent = false,
                };
                db.SchoolYears.Add(year);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.school_year.create", "school_year", year.Id.ToString(), new { name = year.Name }, ct);
                return Results.Created($"/api/admin/school-years/{year.Id}",
                    new SchoolYearAdminDto(year.Id, year.Name, year.StartDate, year.EndDate, year.IsCurrent));
            })
            .WithName("admin.school_years.create")
            .WithSummary("Tạo năm học mới (chưa đặt hiện tại)")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/school-years/{id:long}", async (
                long id, SchoolYearRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var error = ValidateSchoolYear(req);
                if (error is not null)
                    return ApiErrors.Validation(new() { ["body"] = [error] });
                var year = await db.SchoolYears.FirstOrDefaultAsync(y => y.Id == id, ct);
                if (year is null)
                    return ApiErrors.NotFound("Không tìm thấy năm học.");
                year.Name = req!.Name.Trim();
                year.StartDate = req.StartDate;
                year.EndDate = req.EndDate;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.school_year.update", "school_year", year.Id.ToString(), new { name = year.Name }, ct);
                return Results.Ok(new SchoolYearAdminDto(year.Id, year.Name, year.StartDate, year.EndDate, year.IsCurrent));
            })
            .WithName("admin.school_years.update")
            .WithSummary("Sửa năm học (tên, ngày bắt đầu/kết thúc)")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/school-years/{id:long}/rollover", async (
                long id, RolloverRequest req, AdminService svc, IAuditLogger audit, CancellationToken ct) =>
            {
                try
                {
                    var result = await svc.RolloverYearAsync(id, req?.CloneClassesToNextGrade ?? false, ct);
                    await audit.LogAsync("admin.school_year.rollover", "school_year", id.ToString(),
                        new
                        {
                            oldYearId = result.OldYearId,
                            newYearId = result.NewYearId,
                            archivedClasses = result.ArchivedClasses,
                            clonedClasses = result.ClonedClasses,
                            clonedStudents = result.ClonedStudents,
                        }, ct);
                    return Results.Ok(new
                    {
                        oldYearId = result.OldYearId,
                        newYearId = result.NewYearId,
                        archivedClasses = result.ArchivedClasses,
                        clonedClasses = result.ClonedClasses,
                        clonedStudents = result.ClonedStudents,
                    });
                }
                catch (AdminFlowException ex)
                {
                    return AdminUsersEndpoints.AdminFlowResult(ex);
                }
            })
            .WithName("admin.school_years.rollover")
            .WithSummary("Kết chuyển năm học: đặt năm mới hiện tại, lưu trữ lớp năm cũ, tùy chọn nhân bản lên khối +1")
            .RequireAuthorization("Admin");

        // ===== Tags =====
        app.MapGet("/api/admin/tags", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Tags.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new TagAdminDto(t.Id, t.Name, t.Slug))
                .ToListAsync(ct)))
            .WithName("admin.tags.list")
            .WithSummary("Mọi tag")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/tags", async (
                TagRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên tag."] });
                var slug = Slugify.ToSlug(req.Name.Trim());
                if (await db.Tags.AnyAsync(t => t.Slug == slug, ct))
                    return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict, "Tag này đã tồn tại.");
                var tag = new Tag { Name = req.Name.Trim(), Slug = slug };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.tag.create", "tag", tag.Id.ToString(), new { name = tag.Name }, ct);
                return Results.Created($"/api/admin/tags/{tag.Id}", new TagAdminDto(tag.Id, tag.Name, tag.Slug));
            })
            .WithName("admin.tags.create")
            .WithSummary("Thêm tag")
            .RequireAuthorization("Admin");

        app.MapPut("/api/admin/tags/{id:long}", async (
                long id, TagRequest req, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Name))
                    return ApiErrors.Validation(new() { ["name"] = ["Thiếu tên tag."] });
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (tag is null)
                    return ApiErrors.NotFound("Không tìm thấy tag.");
                tag.Name = req.Name.Trim();
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.tag.update", "tag", tag.Id.ToString(), new { name = tag.Name }, ct);
                return Results.Ok(new TagAdminDto(tag.Id, tag.Name, tag.Slug));
            })
            .WithName("admin.tags.update")
            .WithSummary("Sửa tên tag")
            .RequireAuthorization("Admin");

        app.MapDelete("/api/admin/tags/{id:long}", async (
                long id, AppDbContext db, IAuditLogger audit, CancellationToken ct) =>
            {
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (tag is null)
                    return ApiErrors.NotFound("Không tìm thấy tag.");
                db.Tags.Remove(tag); // cascade với document_tags
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("admin.tag.delete", "tag", tag.Id.ToString(), null, ct);
                return Results.NoContent();
            })
            .WithName("admin.tags.delete")
            .WithSummary("Xóa tag (gỡ khỏi mọi tài liệu)")
            .RequireAuthorization("Admin");

        return app;
    }

    private static string? ValidateSection(SectionRequest req)
    {
        if (req.ContentKind is not null && !Enum.TryParse<SectionContentKind>(req.ContentKind, true, out _))
            return "contentKind phải là Document, Quiz hoặc Both.";
        if (req.DefaultPublishMode is not null && !Enum.TryParse<PublishMode>(req.DefaultPublishMode, true, out _))
            return "defaultPublishMode phải là Hidden hoặc Visible.";
        if (req.DefaultScope is not null && !Enum.TryParse<ContentScope>(req.DefaultScope, true, out _))
            return "defaultScope không hợp lệ.";
        return null;
    }

    private static string? ValidateSchoolYear(SchoolYearRequest? req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Name))
            return "Thiếu tên năm học (vd: 2027-2028).";
        if (req.EndDate < req.StartDate)
            return "Ngày kết thúc phải sau ngày bắt đầu.";
        return null;
    }
}

public record SectionAdminDto(long Id, string Slug, string Name, string? Icon, string? Color, short Sort,
    string ContentKind, string DefaultPublishMode, string DefaultScope, bool RequireWeek, bool IsInternal, bool IsActive);
public record SectionRequest(string Name, string? Icon, string? Color, short? Sort,
    string? ContentKind, string? DefaultPublishMode, string? DefaultScope,
    bool? RequireWeek, bool? IsInternal, bool? IsActive);

public record GradeAdminDto(short Id, string Name, short Sort);
public record GradeRequest(string Name, short? Sort);

public record SubjectAdminDto(long Id, string Name, string Slug, short Sort, bool IsActive);
public record SubjectRequest(string Name, short? Sort, bool? IsActive);

public record SchoolYearAdminDto(long Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent);
public record SchoolYearRequest(string Name, DateOnly StartDate, DateOnly EndDate);
public record RolloverRequest(bool CloneClassesToNextGrade = false);

public record TagAdminDto(long Id, string Name, string Slug);
public record TagRequest(string Name);
