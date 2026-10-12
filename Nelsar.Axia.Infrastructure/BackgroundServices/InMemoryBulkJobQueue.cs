using System.Threading.Channels;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Exceptions;

namespace Nelsar.Axia.Infrastructure.BackgroundServices;

internal class InMemoryBulkJobQueue : IBulkJobQueue
{
    /// <summary>
    /// How many jobs may wait for the worker, beside the one it is running.
    ///
    /// The queue used to be unbounded, so anyone allowed to run bulk jobs could keep adding them
    /// — each up to ten thousand rows, all of it held in memory — faster than one worker drains
    /// them, until the process ran out. Jobs run one at a time, so a longer queue only means a
    /// longer wait; a full one is answered with 503 and Retry-After instead.
    /// </summary>
    public const int MaximumPendingJobs = 10;

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly Channel<BulkUserJob> _channel = Channel.CreateBounded<BulkUserJob>(new BoundedChannelOptions(MaximumPendingJobs)
    {
        SingleReader = true,
        SingleWriter = false
    });

    public Task EnqueueAsync(BulkUserJob job, CancellationToken cancellationToken = default)
    {
        // Refused at once rather than waited on: a request that sat until the worker caught up
        // would hold its thread for the length of other people's jobs.
        if (!_channel.Writer.TryWrite(job))
            throw new ServiceBusyException(ApplicationMessages.BulkQueueFull(), RetryAfter);

        return Task.CompletedTask;
    }

    public ValueTask<BulkUserJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
