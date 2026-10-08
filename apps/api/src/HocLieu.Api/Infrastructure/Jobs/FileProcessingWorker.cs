using System.Globalization;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Conversion;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Infrastructure.Jobs;

/// <summary>
/// Xử lý preview file Office → PDF qua Gotenberg (spec §11.2 bước 5).
/// Retry 3 lần, backoff 10s/60s/5m; hết lượt → Failed + processing_error.
/// Khởi động: file Processing (cụt do crash) → Pending, re-enqueue Pending/Processing.
/// </summary>
public sealed class FileProcessingWorker(
    IServiceScopeFactory scopeFactory,
    FileProcessingQueue queue,
    IDocumentConverter converter,
    IFileStorage storage,
    ILogger<FileProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
    ];
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RequeueStaleAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            // DB chưa sẵn sàng (migrate chạy sau?) — không crash host, file sẽ được xử lý khi enqueue lại
            logger.LogError(ex, "Không khởi tạo hàng đợi file; bỏ qua đến lần enqueue kế tiếp");
        }

        while (true)
        {
            long fileId;
            try
            {
                fileId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await ProcessWithRetryAsync(fileId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "File {FileId}: lỗi không xử lý được", fileId);
            }
        }
    }

    private async Task RequeueStaleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stale = await db.Files
            .Where(f => f.ProcessingStatus == ProcessingStatus.Processing)
            .ToListAsync(ct);
        foreach (var f in stale)
            f.ProcessingStatus = ProcessingStatus.Pending;
        await db.SaveChangesAsync(ct);

        var pending = await db.Files
            .Where(f => f.ProcessingStatus == ProcessingStatus.Pending)
            .Select(f => f.Id)
            .ToListAsync(ct);
        foreach (var id in pending)
            queue.Enqueue(id);

        if (stale.Count + pending.Count > 0)
            logger.LogInformation("File worker: {Stale} file Processing → Pending, {Pending} file Pending trong hàng đợi",
                stale.Count, pending.Count);
    }

    private async Task ProcessWithRetryAsync(long fileId, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await ProcessAsync(fileId, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == MaxAttempts)
                {
                    logger.LogError(ex, "File {FileId}: hết {Max} lần thử — đánh dấu Failed", fileId, MaxAttempts);
                    await MarkFailedAsync(fileId, ex.Message, ct);
                    return;
                }

                logger.LogWarning(ex, "File {FileId}: thử {Attempt}/{Max} lỗi — retry sau {Delay}",
                    fileId, attempt, MaxAttempts, RetryDelays[attempt - 1]);
                await Task.Delay(RetryDelays[attempt - 1], ct);
            }
        }
    }

    private async Task ProcessAsync(long fileId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null || file.ProcessingStatus == ProcessingStatus.Ready)
            return;

        file.ProcessingStatus = ProcessingStatus.Processing;
        file.ProcessingAttempts++;
        await db.SaveChangesAsync(ct);

        if (!FileTypes.IsOffice(file.Ext))
        {
            // Pdf/ảnh đã Ready lúc upload; đây là nhánh phòng thủ
            file.ProcessingStatus = ProcessingStatus.Ready;
            file.ProcessingError = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        var original = await storage.GetOriginalBytesAsync(file, ct)
            ?? throw new InvalidOperationException("Không đọc được file gốc từ storage");

        var pdf = await converter.ConvertToPdfAsync(file.OriginalName, original, ct);
        await using (var ms = new MemoryStream(pdf))
            await storage.UploadPreviewPdfAsync(file, ms, ct);

        file.PreviewPages = PdfPages.Count(pdf);
        file.ProcessingStatus = ProcessingStatus.Ready;
        file.ProcessingError = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("File {FileId}: preview PDF {Pages} trang (thử {Attempt})",
            fileId, file.PreviewPages, file.ProcessingAttempts);
    }

    private async Task MarkFailedAsync(long fileId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (file is null)
                return;
            file.ProcessingStatus = ProcessingStatus.Failed;
            file.ProcessingError = error.Length > 500 ? error[..500] : error;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "File {FileId}: không ghi được trạng thái Failed ({Error})", fileId,
                error.Length > 200 ? error[..200] : error);
        }
    }
}
