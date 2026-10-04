using System.Threading.Channels;
using Nelsar.Axia.Application.Services;

namespace Nelsar.Axia.Infrastructure.BackgroundServices;

internal class InMemoryBulkJobQueue : IBulkJobQueue
{
    private readonly Channel<BulkUserJob> _channel = Channel.CreateUnbounded<BulkUserJob>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public async Task EnqueueAsync(BulkUserJob job, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public ValueTask<BulkUserJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
