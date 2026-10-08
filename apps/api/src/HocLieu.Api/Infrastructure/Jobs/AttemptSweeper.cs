using HocLieu.Features.Attempts;

namespace HocLieu.Infrastructure.Jobs;

/// <summary>
/// §6.6: mỗi 5 phút chấm & chuyển Expired các attempt InProgress
/// (quá giờ + 30s, hoặc quiz không còn mở). Khách hết giờ thường tự nộp
/// qua FE; sweeper là lưới an toàn cho tab đóng/mất mạng.
/// </summary>
public sealed class AttemptSweeper(
    IServiceScopeFactory scopeFactory,
    ILogger<AttemptSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<AttemptsService>();
                var count = await svc.SweepOnceAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("AttemptSweeper: đã chốt {Count} lượt làm bỏ dở", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AttemptSweeper: lỗi khi dọn attempt");
            }
        }
    }
}
