using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Application.EventHandlers;

/// <summary>
/// Writes the audit trail. This is the only place that knows user operations are audited —
/// <c>UserService</c> raises the facts and has no idea anyone is listening, which is what
/// lets a second consumer be added without editing the operation itself.
/// </summary>
internal sealed class AuditTrailEventHandler :
    IDomainEventHandler<UserAuthenticationSucceeded>,
    IDomainEventHandler<UserAuthenticationFailed>,
    IDomainEventHandler<UserCreated>,
    IDomainEventHandler<UserUpdated>
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

    public Task HandleAsync(UserCreated domainEvent, CancellationToken cancellationToken = default) =>
        _auditService.LogModificationAsync(domainEvent.UserId, null, domainEvent.Actor, "CreateUser", true, null);

    public Task HandleAsync(UserUpdated domainEvent, CancellationToken cancellationToken = default) =>
        _auditService.LogModificationAsync(domainEvent.UserId, null, domainEvent.Actor, "UpdateUser", true, null);
}
