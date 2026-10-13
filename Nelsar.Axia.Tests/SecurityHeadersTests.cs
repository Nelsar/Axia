using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The headers every API response carries, whichever part of the pipeline answers it. The API
/// sent none: its responses — a session token, directory entries, the audit trail — could be kept
/// by any cache, and a 429 did not say when to come back.
/// </summary>
public class SecurityHeadersTests
{
    private static ApiTestFactory FactoryRefusingEverySignIn()
    {
        var authHandler = new Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>>();
        authHandler.Setup(x => x.Handle(It.IsAny<AuthenticateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthenticateResponse?)null);

        return new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<AuthenticateCommand, AuthenticateResponse?>));
            services.AddSingleton(authHandler.Object);
        });
    }

    private static void AssertProtected(HttpResponseMessage response)
    {
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task AnOrdinaryAnswer_CarriesThem()
    {
        using var factory = FactoryRefusingEverySignIn();

        var response = await factory.CreateClient().GetAsync("/api/v1/providers/selectable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertProtected(response);
    }

    [Fact]
    public async Task ARefusedSignIn_CarriesThem()
    {
        using var factory = FactoryRefusingEverySignIn();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new AuthenticateRequest { Email = "someone@example.org", Password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertProtected(response);
    }

    [Fact]
    public async Task ARateLimitRefusal_CarriesThem_AndSaysWhenToComeBack()
    {
        using var factory = FactoryRefusingEverySignIn();
        var client = factory.CreateClient();
        var request = new AuthenticateRequest { Email = "someone@example.org", Password = "wrong" };

        HttpResponseMessage response;
        var attempts = 0;
        do
        {
            response = await client.PostAsJsonAsync("/api/v1/auth/login", request);
        }
        while (response.StatusCode != HttpStatusCode.TooManyRequests && ++attempts < 20);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertProtected(response);
        // The fallback window is 60 seconds; the wait is within it.
        var retryAfter = response.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    }
}
