using System.Reflection;
using CloudinaryDotNet;
using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HocLieu.Features.Admin;

/// <summary>§5.4 /admin/he-thong (M6): health DB/Cloudinary/Gotenberg, hàng đợi xử lý file, chạy lại job lỗi.</summary>
public static class AdminSystemEndpoints
{
    public static IEndpointRouteBuilder MapAdminSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/system", async (
                AppDbContext db, IConfiguration config, IHttpClientFactory httpFactory, TimeProvider time, CancellationToken ct) =>
            {
                var dbHealthy = await db.Database.CanConnectAsync(ct);

                var cloudinaryUrl = config["Cloudinary:Url"] ?? string.Empty;
                var cloudinary = string.IsNullOrEmpty(cloudinaryUrl)
                    ? "Degraded: Cloudinary chưa cấu hình"
                    : PingCloudinary(cloudinaryUrl);

                var gotenbergUrl = config["Gotenberg:Url"] ?? string.Empty;
                var gotenberg = string.IsNullOrEmpty(gotenbergUrl)
                    ? "Degraded: Gotenberg chưa cấu hình"
                    : await PingGotenbergAsync(httpFactory, gotenbergUrl, ct);

                // Group trong bộ nhớ (an toàn với mọi provider; 20k file là rất nhỏ)
                var statuses = await db.Files.AsNoTracking().Select(f => f.ProcessingStatus).ToListAsync(ct);
                var byStatus = statuses
                    .GroupBy(s => s.ToString())
                    .Select(g => new FileStatusCount(g.Key, g.Count()))
                    .ToList();

                var recentFailed = await db.Files.AsNoTracking()
                    .Where(f => f.ProcessingStatus == ProcessingStatus.Failed)
                    .OrderByDescending(f => f.CreatedAt)
                    .Take(20)
                    .Select(f => new SystemFailedFileRow(f.Id, f.OriginalName, f.ProcessingError, f.ProcessingAttempts, f.CreatedAt))
                    .ToListAsync(ct);

                return Results.Ok(new
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
                    utcNow = time.GetUtcNow().ToString("o"),
                    health = new
                    {
                        db = dbHealthy ? "Healthy" : "Unhealthy",
                        cloudinary,
                        gotenberg,
                    },
                    files = new
                    {
                        pending = Count(byStatus, "Pending"),
                        processing = Count(byStatus, "Processing"),
                        ready = Count(byStatus, "Ready"),
                        failed = Count(byStatus, "Failed"),
                        notApplicable = Count(byStatus, "NotApplicable"),
                    },
                    failedFiles = recentFailed
                        .Select(f => new SystemFailedFileDto(f.Id, f.OriginalName, f.ProcessingError, f.ProcessingAttempts,
                            f.CreatedAt.ToString("o")))
                        .ToList(),
                });
            })
            .WithName("admin.system.get")
            .WithSummary("Trạng thái hệ thống: health, hàng đợi file, phiên bản")
            .RequireAuthorization("Admin");

        app.MapPost("/api/admin/system/files/{id:long}/retry", async (
                long id, AppDbContext db, FileProcessingQueue queue, IAuditLogger audit, CancellationToken ct) =>
            {
                var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
                if (file is null)
                    return ApiErrors.NotFound("Không tìm thấy file.");
                if (file.ProcessingStatus is ProcessingStatus.Ready or ProcessingStatus.NotApplicable)
                    return ApiErrors.Validation(new() { ["id"] = ["File này không cần xử lý lại."] });

                file.ProcessingStatus = ProcessingStatus.Pending;
                file.ProcessingError = null;
                await db.SaveChangesAsync(ct);
                queue.Enqueue(file.Id);
                await audit.LogAsync("admin.file.retry", "file", file.Id.ToString(), null, ct);
                return Results.Ok(new { id = file.Id, status = "Pending" });
            })
            .WithName("admin.system.file_retry")
            .WithSummary("Chạy lại job xử lý file lỗi")
            .RequireAuthorization("Admin");

        return app;
    }

    private static int Count(List<FileStatusCount> list, string status)
        => list.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

    private static string PingCloudinary(string url)
    {
        try
        {
            new Cloudinary(url).Ping();
            return "Healthy";
        }
        catch (Exception ex)
        {
            return "Unhealthy: " + ex.Message;
        }
    }

    private static async Task<string> PingGotenbergAsync(IHttpClientFactory httpFactory, string url, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient("gotenberg");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync("/health", cts.Token);
            return response.IsSuccessStatusCode ? "Healthy" : $"Unhealthy: HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            return "Unhealthy: " + ex.Message;
        }
    }
}

public record FileStatusCount(string Status, int Count);

/// <summary>Hàng thô — format thời gian làm trong bộ nhớ.</summary>
public record SystemFailedFileRow(long Id, string OriginalName, string? ProcessingError,
    short ProcessingAttempts, DateTimeOffset CreatedAt);

public record SystemFailedFileDto(long Id, string OriginalName, string? ProcessingError,
    short ProcessingAttempts, string CreatedAt);
