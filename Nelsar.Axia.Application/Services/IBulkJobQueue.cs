namespace Nelsar.Axia.Application.Services;

public interface IBulkJobQueue
{
    /// <exception cref="Nelsar.Axia.Contracts.Exceptions.ServiceBusyException">
    /// The queue is full. Nothing was queued; the same job can be offered again later.
    /// </exception>
    Task EnqueueAsync(BulkUserJob job, CancellationToken cancellationToken = default);

    ValueTask<BulkUserJob> DequeueAsync(CancellationToken cancellationToken = default);
}
