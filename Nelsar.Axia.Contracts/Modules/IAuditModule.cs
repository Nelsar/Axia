using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>The audit trail, newest first.</summary>
public interface IAuditModule : IOpencoreModule
{
    Task<PagedResponse<AuditEventDto>> GetRecentAsync(
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default);
}
