using Microsoft.Extensions.Caching.Memory;
using Nelsar.Axia.Application.Services;

namespace Nelsar.Axia.Infrastructure.BackgroundServices;

// Backed by IMemoryCache (not a raw ConcurrentDictionary) so completed jobs actually
// expire — a long-running process handling many bulk operations would otherwise leak
// memory forever, since nothing ever removed old entries.
internal class InMemoryBulkJobStore : IBulkJobStore
{
    private const string KeyPrefix = "bulkjob:";

    // Sliding: an actively-polled job (client checking GetBulkJobStatusAsync) doesn't
    // expire mid-flight; an abandoned one is reclaimed a couple hours after the last poll.
    private static readonly MemoryCacheEntryOptions EntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(2)
    };

    private readonly IMemoryCache _cache;

    public InMemoryBulkJobStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Add(BulkUserJob job)
    {
        _cache.Set(KeyPrefix + job.Id, job, EntryOptions);
    }

    public BulkUserJob? Get(string jobId)
    {
        return _cache.TryGetValue(KeyPrefix + jobId, out BulkUserJob? job) ? job : null;
    }
}
