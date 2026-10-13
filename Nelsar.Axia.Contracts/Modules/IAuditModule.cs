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

    /// <summary>
    /// Records an attempt to change something that the endpoint's own permission check refused,
    /// as a failed modification by the signed-in caller.
    ///
    /// The use cases audit themselves, refusals included, but only once they run. An endpoint's
    /// <c>[Authorize(Policy)]</c> refuses before any of them does, so a caller without the
    /// permission at all — the commonest refusal there is — left no trace in the trail.
    /// </summary>
    /// <param name="action">What was attempted, as the host names it: "Users.Create".</param>
    /// <param name="request">The request line, "POST /api/v1/users".</param>
    /// <param name="missingPermissions">The permissions the check found missing.</param>
    Task RecordRefusedAsync(
        string action,
        string request,
        IReadOnlyCollection<string> missingPermissions,
        CancellationToken cancellationToken = default);
}
