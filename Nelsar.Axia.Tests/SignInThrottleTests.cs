using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Api.Authorization;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Infrastructure.RateLimiting;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Failed sign-ins counted per account, from any address.
///
/// The only sign-in limit was per client address, which does nothing against guesses spread over
/// many addresses at one account — each of them a bind against that account in the directory,
/// counting towards its lockout there.
/// </summary>
public class SignInThrottleTests
{
    private static readonly RateLimitPolicySettings ThreeIn15Minutes = new(PermitLimit: 3, WindowSeconds: 900, QueueLimit: 0, Enabled: true);

    private sealed class Clock : TimeProvider
    {
        // Years from the real time on purpose: the window is decided by this clock, not the cache's.
        public DateTimeOffset Now { get; set; } = new(2001, 1, 1, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (SignInThrottle Throttle, Clock Clock) Throttle()
    {
        var clock = new Clock();
        return (new SignInThrottle(new MemoryCache(new MemoryCacheOptions()), clock), clock);
    }

    [Fact]
    public void AnAccountIsRefused_AfterItsAllowanceOfFailures_AndOnlyUntilTheWindowEnds()
    {
        var (throttle, clock) = Throttle();

        for (var i = 0; i < 2; i++)
            throttle.RecordFailure("alice", null, ThreeIn15Minutes);
        Assert.Null(throttle.Blocked("alice", null, ThreeIn15Minutes));

        throttle.RecordFailure("alice", null, ThreeIn15Minutes);
        var wait = throttle.Blocked("alice", null, ThreeIn15Minutes);
        Assert.Equal(TimeSpan.FromMinutes(15), wait);

        clock.Now = clock.Now.AddMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(5), throttle.Blocked("alice", null, ThreeIn15Minutes));

        clock.Now = clock.Now.AddMinutes(5);
        Assert.Null(throttle.Blocked("alice", null, ThreeIn15Minutes));
    }

    [Fact]
    public void ASuccessfulSignIn_ClearsTheCount()
    {
        var (throttle, _) = Throttle();

        throttle.RecordFailure("alice", null, ThreeIn15Minutes);
        throttle.RecordFailure("alice", null, ThreeIn15Minutes);
        throttle.Reset("alice", null);
        throttle.RecordFailure("alice", null, ThreeIn15Minutes);
        throttle.RecordFailure("alice", null, ThreeIn15Minutes);

        Assert.Null(throttle.Blocked("alice", null, ThreeIn15Minutes));
    }

    [Fact]
    public void OneAccount_IsOneLogin_AsTheDirectoryComparesIt_InOneDirectory()
    {
        var (throttle, _) = Throttle();

        throttle.RecordFailure("Alice ", "dir-1", ThreeIn15Minutes);
        throttle.RecordFailure("alice", "DIR-1", ThreeIn15Minutes);
        throttle.RecordFailure(" ALICE", "dir-1", ThreeIn15Minutes);

        Assert.NotNull(throttle.Blocked("alice", "dir-1", ThreeIn15Minutes));
        // The same login in another directory is another account; another login is untouched.
        Assert.Null(throttle.Blocked("alice", "dir-2", ThreeIn15Minutes));
        Assert.Null(throttle.Blocked("bob", "dir-1", ThreeIn15Minutes));
    }

    [Fact]
    public void ADisabledPolicy_CountsNothing_AndRefusesNothing()
    {
        var (throttle, _) = Throttle();
        var disabled = ThreeIn15Minutes with { Enabled = false };

        for (var i = 0; i < 10; i++)
            throttle.RecordFailure("alice", null, disabled);

        Assert.Null(throttle.Blocked("alice", null, disabled));
        Assert.Null(throttle.Blocked("alice", null, ThreeIn15Minutes));
    }

    // --- Through the API ----------------------------------------------------------------------

    private static (ApiTestFactory Factory, Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>> Handler) Api()
    {
        // The right password signs in; anything else is refused, as the directory would.
        var handler = new Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>>();
        handler.Setup(x => x.Handle(It.IsAny<AuthenticateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthenticateCommand command, CancellationToken _) =>
                command.Password == "right" ? new AuthenticateResponse { Token = "t" } : null);

        var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<AuthenticateCommand, AuthenticateResponse?>));
            services.AddSingleton(handler.Object);
        });

        // A per-address limit too high to reach here, so that what is measured is the account's.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RateLimitSettings.AddRange(
            RateLimitSettings.Create("auth-login", 100, 60, 0, true),
            RateLimitSettings.Create(SignInThrottle.PolicyName, 3, 900, 0, true));
        db.SaveChanges();

        return (factory, handler);
    }

    private static Task<HttpResponseMessage> SignIn(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest { Email = login, Password = password });

    [Fact]
    public async Task GuessesAtOneAccount_AreRefusedWithoutAskingTheDirectory_WhileOthersSignInAsUsual()
    {
        var (factory, handler) = Api();
        using var _ = factory;
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(client, "alice", "guess")).StatusCode);

        // Even the right password now: the window is closed for this account.
        var refused = await SignIn(client, "alice", "right");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(refused.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        handler.Verify(x => x.Handle(It.Is<AuthenticateCommand>(c => c.Login == "alice"), It.IsAny<CancellationToken>()), Times.Exactly(3));

        Assert.Equal(HttpStatusCode.OK, (await SignIn(client, "bob", "right")).StatusCode);
    }

    [Fact]
    public async Task ASuccessfulSignIn_StartsTheCountAgain()
    {
        var (factory, _) = Api();
        using var _factory = factory;
        var client = factory.CreateClient();

        await SignIn(client, "alice", "guess");
        await SignIn(client, "alice", "guess");
        Assert.Equal(HttpStatusCode.OK, (await SignIn(client, "alice", "right")).StatusCode);
        await SignIn(client, "alice", "guess");
        await SignIn(client, "alice", "guess");

        Assert.Equal(HttpStatusCode.OK, (await SignIn(client, "alice", "right")).StatusCode);
    }
}
