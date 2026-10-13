using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Infrastructure.BackgroundServices;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Bulk batches had no bounds at all: no limit on the rows in one, none on how many could wait in
/// the queue, and any holder of users.bulk could read any job's results by its id. These pin the
/// bounds and the ownership.
/// </summary>
public class BulkLimitsTests
{
    private static readonly Guid Directory = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherDirectory = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static List<BulkUserRequest> Rows(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new BulkUserRequest { Operation = "create", Email = $"user{i}@example.org", Name = $"User {i}", Password = "P@ssw0rd!" })
            .ToList();

    private static ISender Sender(
        ICurrentUser caller,
        IBulkJobQueue? queue = null,
        IBulkJobStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(new Mock<ILdapService>().Object);
        services.AddSingleton(queue ?? new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(store ?? new Mock<IBulkJobStore>().Object);
        services.AddSingleton(new Mock<Nelsar.Axia.Domain.Repositories.IUserLookupRepository>().Object);
        services.AddSingleton(caller);
        services.AddSingleton<IPermissionResolver>(new FixedPermissionResolver([.. Permissions.All]));
        services.WithAuditTrail();

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ISender>();
    }

    private static readonly TestCurrentUser Alice = new("alice@example.org", Directory);

    // --- Rows per batch -----------------------------------------------------------------------

    [Fact]
    public async Task ABatchProcessedWhileTheCallerWaits_IsLimitedTo100Rows()
    {
        var sender = Sender(Alice);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new ProcessBulkUsersCommand(Rows(ProcessBulkUsersCommand.MaximumSynchronousRows + 1))));

        // It says where a larger batch goes.
        Assert.Contains("/api/v1/users/bulk", refused.Message);
        await sender.Send(new ProcessBulkUsersCommand(Rows(ProcessBulkUsersCommand.MaximumSynchronousRows)));
    }

    [Fact]
    public async Task TheWorkerRunningAQueuedBatch_MayGoPast100()
    {
        // The worker sends the same command; the synchronous limit applied to it would fail every
        // queued job larger than a synchronous one could be.
        var response = await Sender(Alice).Send(new ProcessBulkUsersCommand(Rows(250), Queued: true));

        Assert.Equal(250, response.Items.Count());
    }

    [Fact]
    public async Task AQueuedBatch_IsLimitedToWhatOneGeneratorPreviewProduces()
    {
        var sender = Sender(Alice);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new EnqueueBulkUserJobCommand(Rows(ProcessBulkUsersCommand.MaximumQueuedRows + 1))));
        await sender.Send(new EnqueueBulkUserJobCommand(Rows(ProcessBulkUsersCommand.MaximumQueuedRows)));

        Assert.Equal(GenerateUserPatternsQuery.MaximumItems, ProcessBulkUsersCommand.MaximumQueuedRows);
    }

    // --- The queue ----------------------------------------------------------------------------

    [Fact]
    public async Task AFullQueue_RefusesTheNextJob_AndKeepsNoRecordOfIt()
    {
        var queue = new InMemoryBulkJobQueue();
        var store = new Mock<IBulkJobStore>();
        var sender = Sender(Alice, queue, store.Object);

        for (var i = 0; i < InMemoryBulkJobQueue.MaximumPendingJobs; i++)
            await sender.Send(new EnqueueBulkUserJobCommand(Rows(1)));

        var busy = await Assert.ThrowsAsync<ServiceBusyException>(
            () => sender.Send(new EnqueueBulkUserJobCommand(Rows(1))));

        Assert.True(busy.RetryAfter > TimeSpan.Zero);
        // The refused job was never stored, so it cannot sit in the store as pending for ever.
        store.Verify(x => x.Add(It.IsAny<BulkUserJob>()), Times.Exactly(InMemoryBulkJobQueue.MaximumPendingJobs));
    }

    [Fact]
    public async Task AFullQueue_IsAnswered503_WithRetryAfter()
    {
        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IBulkJobQueue));
            services.AddSingleton<IBulkJobQueue>(new FullQueue());
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.PostAsJsonAsync("/api/v1/users/bulk", Rows(1));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("30", response.Headers.GetValues("Retry-After").Single());
    }

    /// <summary>
    /// A queue with no room, that the worker can still wait on: a mock answered the worker's
    /// dequeue with no job at all, which stopped the host.
    /// </summary>
    private sealed class FullQueue : IBulkJobQueue
    {
        public Task EnqueueAsync(BulkUserJob job, CancellationToken cancellationToken = default) =>
            throw new ServiceBusyException("busy", TimeSpan.FromSeconds(30));

        public async ValueTask<BulkUserJob> DequeueAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }

    // --- Whose job it is ----------------------------------------------------------------------

    private static (Mock<IBulkJobStore> Store, BulkUserJob Job) AlicesJob()
    {
        var job = new BulkUserJob
        {
            Requests = Rows(1),
            ProviderId = Directory,
            RequestedBy = Alice.Subject,
            RequestedByDn = Alice.DistinguishedName
        };
        var store = new Mock<IBulkJobStore>();
        store.Setup(x => x.Get(job.Id)).Returns(job);
        return (store, job);
    }

    [Fact]
    public async Task TheOwner_SeesTheirJob_HoweverTheDnIsSpelled()
    {
        var (store, job) = AlicesJob();
        var aliceAgain = new TestCurrentUser("alice@example.org", Directory,
            distinguishedName: Alice.DistinguishedName!.ToLowerInvariant().Replace(",", ", "));

        Assert.NotNull(await Sender(aliceAgain, store: store.Object).Send(new GetBulkJobStatusQuery(job.Id)));
    }

    [Fact]
    public async Task SomeoneElseWithUsersBulk_IsToldThereIsNoSuchJob()
    {
        var (store, job) = AlicesJob();
        var bob = new TestCurrentUser("bob@example.org", Directory);

        Assert.Null(await Sender(bob, store: store.Object).Send(new GetBulkJobStatusQuery(job.Id)));
    }

    [Fact]
    public async Task TheOwner_SignedInToAnotherDirectory_IsToldThereIsNoSuchJob()
    {
        // One DN can exist in two directories, and need not be the same person in both.
        var (store, job) = AlicesJob();
        var aliceElsewhere = new TestCurrentUser("alice@example.org", OtherDirectory);

        Assert.Null(await Sender(aliceElsewhere, store: store.Object).Send(new GetBulkJobStatusQuery(job.Id)));
    }
}
