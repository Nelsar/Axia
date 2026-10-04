namespace Nelsar.Axia.Domain.Repositories;

/// <summary>
/// Persisted id -> address index backing user-by-id lookups. Unlike the in-process cache it
/// survives a restart and is shared by every instance, which is what turns a directory walk
/// into a point lookup.
/// </summary>
public interface IUserLookupRepository
{
    /// <summary>The indexed address for this id, or null if it was never recorded.</summary>
    Task<string?> FindEmailAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Records or refreshes the mapping. Safe to call repeatedly for the same id.</summary>
    Task RecordAsync(Guid id, string email, CancellationToken cancellationToken = default);

    /// <summary>Drops a mapping that no longer resolves. A no-op when the id is not indexed.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
