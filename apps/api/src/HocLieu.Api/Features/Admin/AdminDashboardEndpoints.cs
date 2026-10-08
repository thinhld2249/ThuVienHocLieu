using HocLieu.Common.OpenApi;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin (M6): dashboard — hàng chờ, cảnh báo, số liệu vận hành.</summary>
public static class AdminDashboardEndpoints
{
    public static IEndpointRouteBuilder MapAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/dashboard", async (AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var now = time.GetUtcNow();
                var d7 = now.AddDays(-7);
                var d30 = now.AddDays(-30);

                var users = await db.Users.AsNoTracking()
                    .GroupBy(u => u.Status)
                    .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                    .ToListAsync(ct);

                var approvalQueue = await db.Users.AsNoTracking()
                    .Where(u => u.Status == Domain.UserStatus.Pending)
                    .OrderBy(u => u.CreatedAt)
                    .Take(50)
                    .Select(u => new ApprovalRow(
                        u.Id, u.FullName, u.Email, u.AvatarUrl,
                        u.RequestedTeamId, u.RequestedTeam == null ? null : u.RequestedTeam.Name, u.CreatedAt))
                    .ToListAsync(ct);

                // Mọi tổ active chưa có Lead → cảnh báo (spec §5.4)
                var leadTeamIds = await db.TeamMembers.AsNoTracking()
                    .Where(m => m.Role == Domain.TeamRole.Lead)
                    .Select(m => (long)m.TeamId)
                    .ToListAsync(ct);
                var teamsWithoutLead = (await db.Teams.AsNoTracking()
                        .Where(t => t.IsActive)
                        .OrderBy(t => t.Name)
                        .Select(t => new DashboardTeamDto(t.Id, t.Name))
                        .ToListAsync(ct))
                    .Where(t => !leadTeamIds.Contains(t.TeamId))
                    .ToList();

                var docs7 = await db.Documents.CountAsync(d => d.CreatedAt >= d7, ct);
                var docs30 = await db.Documents.CountAsync(d => d.CreatedAt >= d30, ct);
                var quizzes7 = await db.Quizzes.CountAsync(q => q.CreatedAt >= d7, ct);
                var quizzes30 = await db.Quizzes.CountAsync(q => q.CreatedAt >= d30, ct);

                var attempts7 = await db.Attempts.CountAsync(a => a.SubmittedAt != null && a.SubmittedAt >= d7, ct);
                var attemptsOpen = await db.Attempts.CountAsync(a => a.Status == Domain.AttemptStatus.InProgress, ct);

                var openReports = await db.ContentReports.CountAsync(r => r.Status == Domain.ReportStatus.Open, ct);

                var totalBytes = await db.Files.SumAsync(f => f.Bytes, ct);
                var fileCount = await db.Files.CountAsync(ct);

                var failedFiles = await db.Files.AsNoTracking()
                    .Where(f => f.ProcessingStatus == Domain.ProcessingStatus.Failed)
                    .OrderByDescending(f => f.CreatedAt)
                    .Take(20)
                    .Select(f => new FailedFileRow(
                        f.Id, f.OriginalName, f.ProcessingError, f.ProcessingAttempts, f.CreatedAt))
                    .ToListAsync(ct);

                return Results.Ok(new
                {
                    users = new
                    {
                        pending = users.Count(u => u.Status == "Pending"),
                        active = users.Count(u => u.Status == "Active"),
                        suspended = users.Count(u => u.Status == "Suspended"),
                        rejected = users.Count(u => u.Status == "Rejected"),
                    },
                    approvalQueue = approvalQueue
                        .Select(u => new DashboardApprovalDto(u.UserId, u.FullName, u.Email, u.AvatarUrl,
                            u.RequestedTeamId, u.RequestedTeamName, u.CreatedAt.ToString("o")))
                        .ToList(),
                    teamsWithoutLead = teamsWithoutLead.Select(t => t.TeamName).ToList(),
                    contentNew = new { documents7d = docs7, quizzes7d = quizzes7, documents30d = docs30, quizzes30d = quizzes30 },
                    attempts = new { last7d = attempts7, inProgress = attemptsOpen },
                    openReports,
                    storage = new { totalBytes = totalBytes, fileCount = fileCount },
                    failedFiles = failedFiles
                        .Select(f => new DashboardFailedFileDto(f.Id, f.OriginalName, f.ProcessingError,
                            f.ProcessingAttempts, f.CreatedAt.ToString("o")))
                        .ToList(),
                });
            })
            .WithName("admin.dashboard")
            .WithSummary("Dashboard admin: hàng chờ duyệt, cảnh báo, số liệu")
            .RequireAuthorization("Admin");

        return app;
    }
}

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record ApprovalRow(long UserId, string FullName, string Email, string? AvatarUrl,
    long? RequestedTeamId, string? RequestedTeamName, DateTimeOffset CreatedAt);

public record FailedFileRow(long Id, string OriginalName, string? ProcessingError,
    short ProcessingAttempts, DateTimeOffset CreatedAt);

public record DashboardApprovalDto(long UserId, string FullName, string Email, string? AvatarUrl,
    long? RequestedTeamId, string? RequestedTeamName, string CreatedAt);

public record DashboardTeamDto(long TeamId, string TeamName);
public record DashboardFailedFileDto(long Id, string OriginalName, string? ProcessingError,
    short ProcessingAttempts, string CreatedAt);
