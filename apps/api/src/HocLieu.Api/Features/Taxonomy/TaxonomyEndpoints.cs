using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Taxonomy;

public record SectionDto(long Id, string Slug, string Name, string? Icon, string? Color, short Sort, string ContentKind, bool RequireWeek, bool IsInternal);
public record GradeDto(short Id, string Name, short Sort);
public record SubjectDto(long Id, string Name, string Slug, short Sort);
public record SchoolYearDto(long Id, string Name, bool IsCurrent, DateOnly StartDate, DateOnly EndDate);
public record TaxonomyDto(IReadOnlyList<SectionDto> Sections, IReadOnlyList<GradeDto> Grades, IReadOnlyList<SubjectDto> Subjects, IReadOnlyList<SchoolYearDto> SchoolYears);

public static class TaxonomyEndpoints
{
    public static IEndpointRouteBuilder MapTaxonomyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/taxonomy", async (AppDbContext db, CancellationToken ct) =>
        {
            var sections = await db.Sections.AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Sort)
                .Select(s => new SectionDto(s.Id, s.Slug, s.Name, s.Icon, s.Color, s.Sort, s.ContentKind.ToString(), s.RequireWeek, s.IsInternal))
                .ToListAsync(ct);

            var grades = await db.Grades.AsNoTracking()
                .OrderBy(g => g.Sort)
                .Select(g => new GradeDto(g.Id, g.Name, g.Sort))
                .ToListAsync(ct);

            var subjects = await db.Subjects.AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Sort)
                .Select(s => new SubjectDto(s.Id, s.Name, s.Slug, s.Sort))
                .ToListAsync(ct);

            var years = await db.SchoolYears.AsNoTracking()
                .OrderByDescending(y => y.StartDate)
                .Select(y => new SchoolYearDto(y.Id, y.Name, y.IsCurrent, y.StartDate, y.EndDate))
                .ToListAsync(ct);

            return Results.Ok(new TaxonomyDto(sections, grades, subjects, years));
        })
        .WithName("public.taxonomy")
        .WithSummary("Danh mục công khai: chuyên mục, khối, môn học, năm học");
        return app;
    }
}
