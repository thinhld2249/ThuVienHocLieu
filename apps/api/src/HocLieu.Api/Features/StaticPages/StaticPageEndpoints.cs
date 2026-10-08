using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.StaticPages;

public record StaticPageDto(string Slug, string Title, string BodyMarkdown, DateTimeOffset? UpdatedAt);

public static class StaticPageEndpoints
{
    public static IEndpointRouteBuilder MapStaticPageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/pages/{slug}", async (string slug, AppDbContext db, CancellationToken ct) =>
        {
            var page = await db.StaticPages.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, ct);
            return page is null
                ? Results.NotFound()
                : Results.Ok(new StaticPageDto(page.Slug, page.Title, page.BodyMarkdown, page.UpdatedAt));
        })
        .WithName("public.page")
        .WithSummary("Trang tĩnh do Admin soạn");
        return app;
    }
}
