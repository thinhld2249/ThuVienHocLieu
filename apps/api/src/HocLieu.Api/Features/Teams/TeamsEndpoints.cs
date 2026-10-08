using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Teams;

public record PublicTeamDto(long Id, string Name, string? GradeName);

public static class TeamsEndpoints
{
    /// <summary>Danh sách tổ đang hoạt động — cho form /cho-duyet (spec §3.1) và mời thành viên (M2).</summary>
    public static IEndpointRouteBuilder MapTeamsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/teams", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Teams.AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new PublicTeamDto(
                    t.Id,
                    t.Name,
                    t.Grade == null ? null : t.Grade.Name))
                .ToListAsync(ct);
            return Results.Ok(rows);
        })
        .WithName("public.teams")
        .WithSummary("Danh sách tổ đang hoạt động");

        return app;
    }
}
