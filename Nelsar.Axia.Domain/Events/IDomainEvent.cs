namespace Nelsar.Axia.Domain.Events;

/// <summary>
/// A business fact that has already happened, stated in the domain's own language.
/// Events are raised by the code that performs the operation and consumed by any number
/// of independent handlers, so a new consumer (audit, metrics, SIEM export, webhook)
/// can be added without touching the code that produced the fact.
/// </summary>
public interface IDomainEvent
{
    /// <summary>When the fact occurred, as opposed to when a handler got round to it.</summary>
    DateTimeOffset OccurredAt { get; }
}
