using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Infrastructure.BackgroundServices;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Which directory a queued batch runs against.
///
/// The worker dequeues on its own thread, where there is no request and therefore no
/// <c>X-LDAP-Provider-Id</c> to read. Until the job carried the answer, there was only one
/// available: the configured default. So a batch queued against the branch domain created every
/// account in the default one and reported the job completed — the same failure the provider
/// factory's fallback was removed to prevent, reached by a different route.
///
/// Two halves, tested as two: the enqueue side has to record the directory while a request is
/// still there to read it from, and the worker side has to make that recorded directory the one
/// its scope resolves to.
/// </summary>
public class BulkJobProviderRoutingTests
{
    private static readonly IReadOnlyList<BulkUserRequest> OneRow =
    [
        new BulkUserRequest { Email = "new.user@branch.example.com", Name = "New User", Password = "P@ssw0rd!" }
    ];

    // ---- Enqueue: the directory is captured while the request can still be asked ----

    private static (ISender Sender, List<BulkUserJob> Queued) EnqueuePipeline(Guid? callerProviderId)
    {
        var queued = new List<BulkUserJob>();

        var queue = new Mock<IBulkJobQueue>();
        queue.Setup(x => x.EnqueueAsync(It.IsAny<BulkUserJob>(), It.IsAny<CancellationToken>()))
            .Callback<BulkUserJob, CancellationToken>((job, _) => queued.Add(job))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(queue.Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("admin@example.com", callerProviderId));
        services.AddSingleton<IPermissionResolver>(
            new FixedPermissionResolver(Permissions.UsersBulk, Permissions.UsersCreate));
        services.WithAuditTrail();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), queued);
    }

    [Fact]
    public async Task Enqueue_RecordsTheDirectoryTheBatchWasAskedFor()
    {
        var branch = Guid.NewGuid();
        var (sender, queued) = EnqueuePipeline(branch);

        await sender.Send(new EnqueueBulkUserJobCommand(OneRow));

        var job = Assert.Single(queued);
        Assert.Equal(branch, job.ProviderId);
    }

    [Fact]
    public async Task Enqueue_RecordsNoDirectory_WhenTheCallerNamedNone()
    {
        // Null is a real answer, not a missing one: a caller who sent no header meant the
        // default, and the batch has to keep running where it always did.
        var (sender, queued) = EnqueuePipeline(callerProviderId: null);

        await sender.Send(new EnqueueBulkUserJobCommand(OneRow));

        Assert.Null(Assert.Single(queued).ProviderId);
    }

    [Fact]
    public async Task Enqueue_RecordsWhoAskedForTheBatch()
    {
        // The worker runs the job as this person; without it the batch ran as nobody.
        var (sender, queued) = EnqueuePipeline(callerProviderId: null);

        await sender.Send(new EnqueueBulkUserJobCommand(OneRow));

        Assert.Equal("admin@example.com", Assert.Single(queued).RequestedBy);
    }

    // ---- Worker: the recorded directory is the one the scope resolves to ----

    /// <summary>
    /// Runs one job through the real background service and reports what
    /// <see cref="LdapProviderScope"/> held at the moment the batch reached the module —
    /// which is what every directory call in that scope would go on to resolve through.
    /// </summary>
    private static async Task<Guid?> ObserveScopeWhileProcessing(BulkUserJob job) =>
        (await ObserveWorkerScope(job)).ProviderId;

    /// <summary>
    /// What the worker's scope held when the batch reached the module: the directory, and the
    /// principal the batch runs as.
    /// </summary>
    private static async Task<(Guid? ProviderId, BackgroundPrincipal Principal)> ObserveWorkerScope(BulkUserJob job)
    {
        var observed = new TaskCompletionSource<(Guid?, BackgroundPrincipal)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddScoped<LdapProviderScope>();
        services.AddScoped<BackgroundPrincipal>();
        services.AddScoped<IUsersModule>(sp =>
        {
            // Resolved from the worker's own scope, so this is the very instance the factory
            // would read — not a second one that happens to agree.
            var scope = sp.GetRequiredService<LdapProviderScope>();
            var principal = sp.GetRequiredService<BackgroundPrincipal>();

            var users = new Mock<IUsersModule>();
            users.Setup(x => x.ProcessBulkAsync(
                    It.IsAny<IEnumerable<BulkUserRequest>?>(), It.IsAny<CancellationToken>()))
                .Callback(() => observed.TrySetResult((scope.ProviderId, principal)))
                .ReturnsAsync(new BulkOperationResponse<BulkUserResult> { Items = Array.Empty<BulkUserResult>() });
            return users.Object;
        });

        var provider = services.BuildServiceProvider();
        var queue = new InMemoryBulkJobQueue();

        var worker = new BulkUserBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BulkUserBackgroundService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.EnqueueAsync(job);

            // Bounded so a regression that never dispatches fails the test rather than hanging it.
            var finished = await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(ReferenceEquals(finished, observed.Task), "The worker never dispatched the batch.");

            return await observed.Task;
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TheWorker_RunsTheBatchAgainstTheDirectoryTheJobNames()
    {
        // The regression this whole file exists for. Before the job carried a directory, this
        // came back null and every row landed in the default domain.
        var branch = Guid.NewGuid();

        var resolved = await ObserveScopeWhileProcessing(new BulkUserJob
        {
            Requests = [.. OneRow],
            ProviderId = branch
        });

        Assert.Equal(branch, resolved);
    }

    [Fact]
    public async Task TheWorker_LeavesTheDirectoryUnset_WhenTheJobNamesNone()
    {
        var resolved = await ObserveScopeWhileProcessing(new BulkUserJob
        {
            Requests = [.. OneRow],
            ProviderId = null
        });

        Assert.Null(resolved);
    }

    [Fact]
    public async Task TheWorker_RunsTheBatchAsThePersonWhoQueuedIt()
    {
        var branch = Guid.NewGuid();

        var (_, principal) = await ObserveWorkerScope(new BulkUserJob
        {
            Requests = [.. OneRow],
            ProviderId = branch,
            RequestedBy = "helpdesk@example.com"
        });

        Assert.True(principal.IsSet);
        Assert.Equal("helpdesk@example.com", principal.Subject);
        Assert.Equal(branch, principal.ProviderId);
    }

    // ---- The seam itself ----

    [Fact]
    public void TheScope_StartsUnset()
    {
        // What every HTTP request sees. Nothing sets it there, and the factory must go on
        // reading the header rather than this.
        Assert.Null(new LdapProviderScope().ProviderId);
    }

    [Fact]
    public void TheScope_HoldsWhatTheDispatcherNamed()
    {
        var scope = new LdapProviderScope();
        var branch = Guid.NewGuid();

        scope.Use(branch);

        Assert.Equal(branch, scope.ProviderId);
    }

    /// <summary>
    /// Guards the wiring rather than a behaviour: the scope and the factory that reads it have to
    /// be the same instance within one unit of work. Registered as anything but scoped — a
    /// singleton in particular — a worker would leak its directory into the next job, or into a
    /// request.
    /// </summary>
    [Fact]
    public void TheScope_IsOnePerUnitOfWork()
    {
        var services = new ServiceCollection();
        services.AddScoped<LdapProviderScope>();
        var provider = services.BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var inFirst = first.ServiceProvider.GetRequiredService<LdapProviderScope>();

        Assert.Same(inFirst, first.ServiceProvider.GetRequiredService<LdapProviderScope>());
        Assert.NotSame(inFirst, second.ServiceProvider.GetRequiredService<LdapProviderScope>());
    }
}
