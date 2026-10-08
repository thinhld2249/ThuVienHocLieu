using System.Security.Cryptography;
using System.Text;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Infrastructure.Data;

namespace HocLieu.Features.Documents;

/// <summary>
/// Khu công khai (spec §5.1, §10): danh sách gộp theo loại, chi tiết tài liệu,
/// tài liệu liên quan, báo lỗi nội dung. Quyền xem theo VisibleTo — không thấy = 404.
/// </summary>
public static class PublicContentEndpoints
{
    public static IEndpointRouteBuilder MapPublicContentEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== Danh sách gộp: tài liệu + bài tập =====
        app.MapGet("/api/public/items", async (
            string? kind, string? section, short? grade, string? subject,
            long? year, short? week, string? q, string? sort,
            int? page, int? pageSize,
            AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var (p, ps) = Pagination.Parse(page, pageSize);
            var filter = new DocumentsService.PublicListFilter(
                kind, section, grade, subject, year, week, q, sort, p, ps);

            var (all, total) = await svc.ListPublicAsync(viewer, filter, ct);
            var items = all
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToList();

            return Results.Ok(new PagedResult<PublicItemRow>(items, total, p, ps));
        })
        .WithName("public.items")
        .WithSummary("Danh sách tài liệu/bài tập công khai (theo kind) + bộ lọc + tìm kiếm")
        .WithMetadata(new QueryParameter("kind", "string"))
        .WithMetadata(new QueryParameter("section", "string"))
        .WithMetadata(new QueryParameter("grade", "integer"))
        .WithMetadata(new QueryParameter("subject", "string"))
        .WithMetadata(new QueryParameter("year", "integer"))
        .WithMetadata(new QueryParameter("week", "integer"))
        .WithMetadata(new QueryParameter("q", "string"))
        .WithMetadata(new QueryParameter("sort", "string"))
        .WithMetadata(new QueryParameter("page", "integer"))
        .WithMetadata(new QueryParameter("pageSize", "integer"));

        // ===== Chi tiết tài liệu công khai =====
        app.MapGet("/api/public/documents/{id:long}", async (
            long id, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var detail = await svc.GetPublicDetailAsync(viewer, id, ct);
            return detail is null
                ? ApiErrors.NotFound("Không tìm thấy tài liệu")
                : Results.Ok(detail);
        })
        .WithName("public.document")
        .WithSummary("Chi tiết tài liệu công khai (tăng lượt xem)");

        // ===== Tài liệu liên quan =====
        app.MapGet("/api/public/documents/{id:long}/related", async (
            long id, AppDbContext db, DocumentsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var related = await svc.GetRelatedAsync(viewer, id, ct);
            return Results.Ok(related);
        })
        .WithName("public.document.related")
        .WithSummary("Tài liệu liên quan (cùng chuyên mục + khối)");

        // ===== Báo lỗi nội dung =====
        app.MapPost("/api/public/reports", async (
            ReportRequest req, IConfiguration config, AppDbContext db, DocumentsService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var viewer = await CurrentUserService.GetViewerAsync(ctx, db, ct);
            var salt = config["Security:IpHashSalt"] ?? string.Empty;
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            var ipHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip + salt))).ToLowerInvariant();

            var (ok, code) = await svc.SubmitReportAsync(viewer, req, ipHash, ct);
            if (!ok)
            {
                return code == "validation"
                    ? ApiErrors.Validation(new() { ["reason"] = ["Vui lòng nhập lý do báo lỗi"] })
                    : ApiErrors.NotFound("Không tìm thấy nội dung để báo lỗi");
            }
            return Results.NoContent();
        })
        .RequireRateLimiting(RateLimits.Report)
        .WithName("public.report")
        .WithSummary("Báo lỗi/vi phạm nội dung (khách được phép)");

        return app;
    }
}
