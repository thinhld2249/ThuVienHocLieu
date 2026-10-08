using System.Threading.Channels;

namespace HocLieu.Infrastructure.Jobs;

/// <summary>
/// Hàng đợi xử lý file trong process (spec §8.1: không message broker).
/// DB là nguồn sự thật — worker khởi động lại sẽ re-enqueue mọi file Pending/Processing.
/// </summary>
public sealed class FileProcessingQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(long fileId) => _channel.Writer.TryWrite(fileId);

    public ValueTask<long> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
