using System.Globalization;
using System.Text;
using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/nhat-ky (M6): audit log — lọc theo người/hành động/thời gian, xuất CSV.</summary>
public static class AdminAuditEndpoints
{
    private const int CsvMaxRows = 10_000;

    public static IEndpointRouteBuilder MapAdminAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/audit-logs", async (
                AppDbContext db,
                [FromQuery] long? actorUserId, [FromQuery] string? action,
                [FromQuery] string? from, [FromQuery] string? to,
                [FromQuery] int? page, [FromQuery] int? pageSize,
                CancellationToken ct) =>
            {
                var (query, error) = BuildQuery(db, actorUserId, action, from, to);
                if (error is not null)
                    return ApiErrors.Validation(error);

                (var p, var ps) = Pagination.Parse(page, pageSize, 50);
                var total = await query.CountAsync(ct);
                var rows = await query
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip((p - 1) * ps).Take(ps)
                    .Select(a => new AuditRow(
                        a.Id, a.ActorUserId, a.Actor == null ? null : a.Actor.FullName,
                        a.Action, a.EntityType, a.EntityId, a.Data, a.Ip, a.CreatedAt))
                    .ToListAsync(ct);

                var items = rows
                    .Select(r => new AdminAuditLogDto(r.Id, r.ActorUserId, r.ActorName,
                        r.Action, r.EntityType, r.EntityId, r.Data, r.Ip, r.CreatedAt.ToString("o")))
                    .ToList();
                return Results.Ok(new PagedResult<AdminAuditLogDto>(items, total, p, ps));
            })
            .WithName("admin.audit_logs.list")
            .WithSummary("Nhật ký hệ thống (lọc người dùng/hành động/thời gian)")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("actorUserId", "integer"))
            .WithMetadata(new QueryParameter("action"))
            .WithMetadata(new QueryParameter("from"))
            .WithMetadata(new QueryParameter("to"))
            .WithMetadata(new QueryParameter("page", "integer"))
            .WithMetadata(new QueryParameter("pageSize", "integer"));

        app.MapGet("/api/admin/audit-logs.csv", async (
                AppDbContext db, IAuditLogger audit,
                [FromQuery] long? actorUserId, [FromQuery] string? action,
                [FromQuery] string? from, [FromQuery] string? to,
                CancellationToken ct) =>
            {
                var (query, error) = BuildQuery(db, actorUserId, action, from, to);
                if (error is not null)
                    return ApiErrors.Validation(error);

                var rows = await query
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(CsvMaxRows)
                    .Select(a => new AuditRow(
                        a.Id, a.ActorUserId, a.Actor == null ? null : a.Actor.FullName,
                        a.Action, a.EntityType, a.EntityId, a.Data, a.Ip, a.CreatedAt))
                    .ToListAsync(ct);

                var sb = new StringBuilder();
                sb.AppendLine("Id;Người dùng;Hành động;Đối tượng;Mã đối tượng;Dữ liệu;IP;Thời gian");
                foreach (var r in rows)
                    sb.AppendLine(string.Join(';',
                        r.Id,
                        Csv(r.ActorName),
                        Csv(r.Action),
                        Csv(r.EntityType),
                        Csv(r.EntityId),
                        Csv(r.Data),
                        Csv(r.Ip),
                        r.CreatedAt.ToString("o", CultureInfo.InvariantCulture)));

                await audit.LogAsync("admin.audit_export", "audit_log", null, new { rows = rows.Count }, ct);
                return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "nhat-ky-hoc-lieu.csv");
            })
            .WithName("admin.audit_logs.csv")
            .WithSummary("Xuất CSV nhật ký (tối đa 10.000 dòng)")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("actorUserId", "integer"))
            .WithMetadata(new QueryParameter("action"))
            .WithMetadata(new QueryParameter("from"))
            .WithMetadata(new QueryParameter("to"));

        return app;
    }

    private static (IQueryable<AuditLog> Query, Dictionary<string, string[]>? Error) BuildQuery(
        AppDbContext db, long? actorUserId, string? action, string? from, string? to)
    {
        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();
        if (actorUserId is not null)
            query = query.Where(a => a.ActorUserId == actorUserId);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action!.Trim());
        if (from is { Length: > 0 })
        {
            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromV))
                return (query, new() { ["from"] = ["Thời gian bắt đầu không hợp lệ."] });
            query = query.Where(a => a.CreatedAt >= fromV);
        }
        if (to is { Length: > 0 })
        {
            if (!DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toV))
                return (query, new() { ["to"] = ["Thời gian kết thúc không hợp lệ."] });
            query = query.Where(a => a.CreatedAt <= toV);
        }
        return (query, null);
    }

    private static string Csv(string? value)
        => value is null ? "" : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record AuditRow(
    long Id, long? ActorUserId, string? ActorName,
    string Action, string? EntityType, string? EntityId, string? Data, string? Ip, DateTimeOffset CreatedAt);

public record AdminAuditLogDto(
    long Id, long? ActorUserId, string? ActorName,
    string Action, string? EntityType, string? EntityId, string? Data, string? Ip, string CreatedAt);
