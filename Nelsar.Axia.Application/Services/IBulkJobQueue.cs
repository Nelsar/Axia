namespace Nelsar.Axia.Application.Services;

public interface IBulkJobQueue
{
    Task EnqueueAsync(BulkUserJob job, CancellationToken cancellationToken = default);
    ValueTask<BulkUserJob> DequeueAsync(CancellationToken cancellationToken = default);
}
