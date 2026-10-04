using System;
using System.Threading.Tasks;

namespace Nelsar.Axia.Application.Interfaces
{
    /// <summary>
    /// Appends to the audit trail.
    ///
    /// Write-only by design. Entries are produced by AuditTrailEventHandler reacting to domain
    /// events, never by a client request, so there is no use case to express them as — whereas
    /// reading the trail is one, and lives in GetRecentAuditEventsQuery.
    /// </summary>
    public interface IAuditService
    {
        Task LogAuthenticationAsync(string actor, bool success, string? dn = null, string? details = null);
        Task LogModificationAsync(Guid? objectId, string? objectDn, string actor, string action, bool success, string? details = null);
    }
}
