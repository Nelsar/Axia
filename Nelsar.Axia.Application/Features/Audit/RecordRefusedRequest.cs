using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;

namespace Nelsar.Axia.Application.Features.Audit;

/// <summary>
/// An attempt to change something that the endpoint's own permission check refused, before any
/// use case ran — so AuditBehaviour, which audits the use cases, never saw it.
///
/// Sent by the host, never by a client. Deliberately not a <c>*Command</c>: it changes nothing
/// but the trail and is the trail's own write, and an <see cref="IAuditedCommand"/> would be
/// recorded a second time, as the recording of itself.
/// </summary>
/// <param name="Action">What was attempted, as the host names it: "Users.Create".</param>
/// <param name="Request">The request line, "POST /api/v1/users".</param>
/// <param name="MissingPermissions">What the caller lacked for it.</param>
internal sealed record RecordRefusedRequest(
    string Action,
    string Request,
    IReadOnlyCollection<string> MissingPermissions) : IRequest;

internal sealed class RecordRefusedRequestHandler : IRequestHandler<RecordRefusedRequest>
{
    private readonly IAuditService _audit;
    private readonly ActingUser _actingUser;

    public RecordRefusedRequestHandler(IAuditService audit, ActingUser actingUser)
    {
        _audit = audit;
        _actingUser = actingUser;
    }

    /// <summary>
    /// Laid out as AuditBehaviour lays out a refusal, so the two read alike in the trail. The
    /// actor is the principal, never anything the request says.
    /// </summary>
    public Task Handle(RecordRefusedRequest request, CancellationToken cancellationToken)
    {
        var details = string.Join("; ",
            request.Request,
            AuditDetails.Directory(_actingUser.ProviderId),
            $"refused before it ran: requires {string.Join(", ", request.MissingPermissions)}");

        return _audit.LogModificationAsync(
            objectId: null,
            objectDn: null,
            actor: _actingUser.Subject ?? string.Empty,
            request.Action,
            success: false,
            details);
    }
}
