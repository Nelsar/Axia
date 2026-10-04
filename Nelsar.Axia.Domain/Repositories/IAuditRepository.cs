using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Domain.Repositories;

public interface IAuditRepository
{
    Task AddAsync(AuditEvent evt);

    /// <summary>Most recent events first, paged via offset/pageSize.</summary>
    Task<(IReadOnlyList<AuditEvent> Items, bool HasMore)> GetRecentAsync(int offset, int pageSize);
}
