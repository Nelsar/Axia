using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Application.Interfaces;

/// <summary>
/// Reacts to a single kind of domain event. Handlers are independent of each other and
/// of the code that raised the event: one failing handler must not stop the others, and
/// none of them may fail the originating request (see <see cref="IDomainEventPublisher"/>).
/// </summary>
public interface IDomainEventHandler<TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
