using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Infrastructure.Events;

/// <summary>
/// Resolves handlers from the current scope and runs them in sequence.
///
/// Each handler is isolated: a failure is logged and the remaining handlers still run.
/// That is deliberate — these events are raised after the operation has already succeeded
/// (the user is authenticated, the account is created), so a broken audit sink must not
/// turn a successful request into a 500. It must not vanish silently either, hence the log.
/// </summary>
internal sealed class DomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventPublisher> _logger;

    public DomainEventPublisher(IServiceProvider serviceProvider, ILogger<DomainEventPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        foreach (var handler in _serviceProvider.GetServices<IDomainEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Domain event handler {Handler} failed for {EventType}.",
                    handler.GetType().Name,
                    typeof(TEvent).Name);
            }
        }
    }
}
