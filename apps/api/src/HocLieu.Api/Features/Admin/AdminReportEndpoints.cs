using System.Security.Claims;
using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/bao-cao (M6): báo lỗi/vi phạm từ người xem — xem, xử lý, ghi chú.</summary>
public static class AdminReportEndpoints
{
    public static IEndpointRouteBuilder MapAdminReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/reports", async (
                AppDbContext db,
                [FromQuery] string? status,
                [FromQuery] int? page, [FromQuery] int? pageSize,
                CancellationToken ct) =>
            {
                IQueryable<ContentReport> query = db.ContentReports.AsNoTracking();
                if (Enum.TryParse<ReportStatus>(status, true, out var st))
                    query = query.Where(r => r.Status == st);

                (var p, var ps) = Pagination.Parse(page, pageSize, 25);
                var total = await query.CountAsync(ct);
                var rows = await query
                    .OrderByDescending(r => r.CreatedAt)
                    .Skip((p - 1) * ps).Take(ps)
                    .Select(r => new ReportRow(
                        r.Id, r.ItemType.ToString(), r.ItemId, r.Reason, r.Detail,
                        r.ReporterUserId, r.Reporter == null ? null : r.Reporter.FullName, r.Status.ToString(), r.Note, r.CreatedAt))
                    .ToListAsync(ct);

                // Tên nội dung bị báo (kể cả đã xóa)
                var docIds = rows.Where(r => r.ItemType == "Document").Select(r => r.ItemId).ToList();
                var quizIds = rows.Where(r => r.ItemType == "Quiz").Select(r => r.ItemId).ToList();
                var docTitles = await db.Documents.IgnoreQueryFilters()
                    .Where(d => docIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, d => d.Title, ct);
                var quizTitles = await db.Quizzes.IgnoreQueryFilters()
                    .Where(q => quizIds.Contains(q.Id))
                    .ToDictionaryAsync(q => q.Id, q => q.Title, ct);

                var items = rows.Select(r =>
                {
                    string? title = r.ItemType == "Document"
                        ? docTitles.GetValueOrDefault(r.ItemId)
                        : quizTitles.GetValueOrDefault(r.ItemId);
                    return new AdminReportDto(r.Id, r.ItemType, r.ItemId, title,
                        r.Reason, r.Detail, r.ReporterUserId, r.ReporterName,
                        r.Status, r.Note, r.CreatedAt.ToString("o"));
                }).ToList();

                return Results.Ok(new PagedResult<AdminReportDto>(items, total, p, ps));
            })
            .WithName("admin.reports.list")
            .WithSummary("Báo lỗi nội dung từ người xem")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("status"))
            .WithMetadata(new QueryParameter("page", "integer"))
            .WithMetadata(new QueryParameter("pageSize", "integer"));

        app.MapPatch("/api/admin/reports/{id:long}", async (
                long id, ReportResolveRequest req, AppDbContext db, HttpContext ctx, IAuditLogger audit, TimeProvider time, CancellationToken ct) =>
            {
                if (req is null || !Enum.TryParse<ReportStatus>(req.Status, true, out var st)
                    || st is not (ReportStatus.Resolved or ReportStatus.Dismissed))
                    return ApiErrors.Validation(new() { ["status"] = ["status phải là \"Resolved\" hoặc \"Dismissed\"."] });

                var report = await db.ContentReports.FirstOrDefaultAsync(r => r.Id == id, ct);
                if (report is null)
                    return ApiErrors.NotFound("Không tìm thấy báo cáo.");

                long? actorId = null;
                if (ctx.User.FindFirst(HocLieu.Common.Auth.CurrentUserService.ClaimUid)?.Value is { Length: > 0 } uid
                    && long.TryParse(uid, out var uidL))
                    actorId = uidL;
                report.Status = st;
                report.ResolvedBy = actorId;
                report.ResolvedAt = time.GetUtcNow();
                if (req.Note is not null)
                    report.Note = req.Note;
                await db.SaveChangesAsync(ct);

                await audit.LogAsync(
                    st == ReportStatus.Resolved ? "admin.report.resolve" : "admin.report.dismiss",
                    "content_report", report.Id.ToString(),
                    new { status = st.ToString(), note = req.Note }, ct);
                return Results.Ok(new { id = report.Id, status = st.ToString() });
            })
            .WithName("admin.reports.resolve")
            .WithSummary("Xử lý (duyệt/bỏ qua) báo cáo, ghi chú")
            .RequireAuthorization("Admin");

        return app;
    }
}

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record ReportRow(
    long Id, string ItemType, long ItemId, string Reason, string? Detail,
    long? ReporterUserId, string? ReporterName, string Status, string? Note, DateTimeOffset CreatedAt);

public record AdminReportDto(
    long Id, string ItemType, long ItemId, string? ItemTitle,
    string Reason, string? Detail,
    long? ReporterUserId, string? ReporterName,
    string Status, string? Note, string CreatedAt);

public record ReportResolveRequest(string? Status, string? Note);
