using MediatR;
using Nelsar.Axia.Application.Features.RateLimitSettings.Queries;
using Nelsar.Axia.Application.Features.RateLimitSettings.Commands;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

public class RateLimitSettingsControllerIntegrationTests
{
    [Fact]
    public async Task GetAll_ReturnsSettings_ForAdmin()
    {
        var getAllHandler = new Mock<IRequestHandler<GetAllRateLimitSettingsQuery, IEnumerable<RateLimitSettingsDto>>>();
        getAllHandler.Setup(x => x.Handle(It.IsAny<GetAllRateLimitSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RateLimitSettingsDto(Guid.NewGuid(), "auth-login", 5, 60, 0, true) });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<GetAllRateLimitSettingsQuery, IEnumerable<RateLimitSettingsDto>>));
            services.AddSingleton(getAllHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.GetAsync("/api/v1/rate-limit-settings");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<List<RateLimitSettingsDto>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("auth-login", result[0].PolicyName);
    }

    [Fact]
    public async Task GetAll_ReturnsForbidden_WithoutThePermission()
    {

        using var factory = new ApiTestFactory(services =>
        {
            // Signed in, but holding nothing — the endpoint must refuse on the permission,
            // not on a role name it no longer looks at.
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver>(_ => new FixedPermissionResolver());
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.User));

        var response = await client.GetAsync("/api/v1/rate-limit-settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_ReturnsUpdatedSettings()
    {
        var updated = new RateLimitSettingsDto(Guid.NewGuid(), "auth-login", 15, 60, 0, true);
        var upsertHandler = new Mock<IRequestHandler<UpsertRateLimitSettingsCommand, RateLimitSettingsDto>>();
        upsertHandler.Setup(x => x.Handle(It.IsAny<UpsertRateLimitSettingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<UpsertRateLimitSettingsCommand, RateLimitSettingsDto>));
            services.AddSingleton(upsertHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.PutAsJsonAsync("/api/v1/rate-limit-settings/auth-login", new RateLimitSettingsUpdateRequest
        {
            PermitLimit = 15,
            WindowSeconds = 60,
            QueueLimit = 0,
            Enabled = true
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<RateLimitSettingsDto>();
        Assert.NotNull(payload);
        Assert.Equal(15, payload.PermitLimit);
    }
}
