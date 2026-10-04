using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Application.Interfaces;

/// <summary>
/// Delivers a domain event to every registered <see cref="IDomainEventHandler{TEvent}"/>.
///
/// Publishing never throws on a handler's behalf: side effects like audit logging must not
/// be able to fail an operation that has already succeeded. Implementations are expected to
/// isolate each handler and log its failure rather than propagate it.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
