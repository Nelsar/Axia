using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Features.RateLimitSettings.Queries;
using Nelsar.Axia.Application.Features.RateLimitSettings.Commands;
using Nelsar.Axia.Application.Features.RoleMappings.Queries;
using Nelsar.Axia.Application.Features.RoleMappings.Commands;
using Nelsar.Axia.Application.Features.Providers.Queries;
using Nelsar.Axia.Application.Features.Providers.Commands;
using Nelsar.Axia.Application;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Moq;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Infrastructure.RateLimiting;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Tests;

public class RateLimitSettingsServiceTests
{
    private static ISender CreateSender(IRateLimitSettingsRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(repo);
        // These requests declare ratelimits.manage; AuthorizationBehaviour refuses without it.
        services.WithAuthorizedCaller();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task UpsertAsync_MapsAndReturnsDto()
    {
        var repo = new Mock<IRateLimitSettingsRepository>();
        // RateLimitSettings.Create issues the id, so the repository has none to assign.
        repo.Setup(x => x.UpsertAsync(It.IsAny<RateLimitSettings>())).ReturnsAsync((RateLimitSettings s) => s);

        var sender = CreateSender(repo.Object);

        var result = await sender.Send(new UpsertRateLimitSettingsCommand("auth-login", 10, 120, 2, true));

        Assert.Equal("auth-login", result.PolicyName);
        Assert.Equal(10, result.PermitLimit);
        Assert.Equal(120, result.WindowSeconds);
    }

    [Fact]
    public async Task GetByPolicyNameAsync_ReturnsNull_WhenMissing()
    {
        var repo = new Mock<IRateLimitSettingsRepository>();
        repo.Setup(x => x.GetByPolicyNameAsync("unknown")).ReturnsAsync((RateLimitSettings?)null);

        var sender = CreateSender(repo.Object);

        Assert.Null(await sender.Send(new GetRateLimitSettingsByPolicyQuery("unknown")));
    }
}
