using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Infrastructure.Events;

namespace Nelsar.Axia.Tests;

public class DomainEventPublisherTests
{
    private sealed class RecordingHandler : IDomainEventHandler<UserCreated>
    {
        public List<UserCreated> Handled { get; } = new();

        public Task HandleAsync(UserCreated domainEvent, CancellationToken cancellationToken = default)
        {
            Handled.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<UserCreated>
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(UserCreated domainEvent, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("audit sink is down");
        }
    }

    private static DomainEventPublisher CreatePublisher(IServiceProvider serviceProvider) =>
        new(serviceProvider, NullLogger<DomainEventPublisher>.Instance);

    [Fact]
    public async Task PublishAsync_InvokesEveryRegisteredHandler()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<UserCreated>>(first);
        services.AddSingleton<IDomainEventHandler<UserCreated>>(second);
        await using var provider = services.BuildServiceProvider();

        var domainEvent = new UserCreated(Guid.NewGuid(), "admin@example.com");
        await CreatePublisher(provider).PublishAsync(domainEvent);

        Assert.Same(domainEvent, Assert.Single(first.Handled));
        Assert.Same(domainEvent, Assert.Single(second.Handled));
    }

    [Fact]
    public async Task PublishAsync_IsolatesFailingHandler_SoLaterHandlersStillRun()
    {
        var throwing = new ThrowingHandler();
        var recording = new RecordingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<UserCreated>>(throwing);
        services.AddSingleton<IDomainEventHandler<UserCreated>>(recording);
        await using var provider = services.BuildServiceProvider();

        // Must not throw: the user was already created, a broken audit sink cannot undo that.
        await CreatePublisher(provider).PublishAsync(new UserCreated(Guid.NewGuid(), "admin@example.com"));

        Assert.True(throwing.WasCalled);
        Assert.Single(recording.Handled);
    }

    [Fact]
    public async Task PublishAsync_IsNoOp_WhenNothingSubscribes()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await CreatePublisher(provider).PublishAsync(new UserUpdated(Guid.NewGuid(), "admin@example.com"));
    }

    [Fact]
    public async Task PublishAsync_RejectsNullEvent()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreatePublisher(provider).PublishAsync<UserCreated>(null!));
    }
}
