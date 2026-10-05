using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Application.EventHandlers;

/// <summary>
/// Writes sign-ins to the audit trail. Authentication raises the facts and has no idea anyone is
/// listening, which is what lets a second consumer be added without editing the operation itself.
///
/// It used to record account creation and updates too, from the UserCreated and UserUpdated
/// events — with the changed account's address as the actor, since that is all the events carry.
/// Changes are now recorded by AuditBehaviour, which knows who made them.
/// </summary>
internal sealed class AuditTrailEventHandler :
    IDomainEventHandler<UserAuthenticationSucceeded>,
    IDomainEventHandler<UserAuthenticationFailed>
{
    private readonly IAuditService _auditService;

    public AuditTrailEventHandler(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public Task HandleAsync(UserAuthenticationSucceeded domainEvent, CancellationToken cancellationToken = default) =>
        _auditService.LogAuthenticationAsync(domainEvent.Actor, true, domainEvent.Dn, null);

    public Task HandleAsync(UserAuthenticationFailed domainEvent, CancellationToken cancellationToken = default) =>
        _auditService.LogAuthenticationAsync(domainEvent.Actor, false, null, domainEvent.Reason);
}
