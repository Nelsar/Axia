using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Features.Users.Commands;
using MediatR;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Tests;

public class AuthRateLimitingIntegrationTests
{
    [Fact]
    public async Task Login_ReturnsTooManyRequests_AfterExceedingDefaultFallbackLimit()
    {
        // No RateLimitSettings row is seeded in the Testing environment (DbInitializer
        // is skipped there), so this also proves RateLimitSettingsProvider's in-code
        // fallback (5 requests / 60s) kicks in and is actually enforced end-to-end.
        var authHandler = new Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>>();
        authHandler.Setup(x => x.Handle(It.IsAny<AuthenticateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthenticateResponse?)null);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<AuthenticateCommand, AuthenticateResponse?>));
            services.AddSingleton(authHandler.Object);
        });

        var client = factory.CreateClient();
        var request = new AuthenticateRequest { Email = "attacker@example.com", Password = "wrong" };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", request);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(5), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal((HttpStatusCode)429, statuses[5]);
    }

    [Fact]
    public async Task Login_StaysUnderLimit_WhenRequestCountIsBelowThreshold()
    {
        var authHandler = new Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>>();
        authHandler.Setup(x => x.Handle(It.IsAny<AuthenticateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthenticateResponse?)null);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<AuthenticateCommand, AuthenticateResponse?>));
            services.AddSingleton(authHandler.Object);
        });

        var client = factory.CreateClient();
        var request = new AuthenticateRequest { Email = "user@example.com", Password = "wrong" };

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
