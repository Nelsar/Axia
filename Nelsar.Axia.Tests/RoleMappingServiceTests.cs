using Nelsar.Axia.Domain.Authorization;
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
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Tests;

public class RoleMappingServiceTests
{
    private static ISender CreateSender(IRoleMappingRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(repo);
        // These requests declare roles.manage; AuthorizationBehaviour refuses without a holder.
        services.WithAuthorizedCaller();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task CreateAsync_MapsAndReturnsDto()
    {
        var repo = new Mock<IRoleMappingRepository>();
        // RoleMapping.Create issues the id, so the repository has none to assign.
        repo.Setup(x => x.AddAsync(It.IsAny<RoleMapping>())).ReturnsAsync((RoleMapping m) => m);

        var sender = CreateSender(repo.Object);

        var result = await sender.Send(new CreateRoleMappingCommand(null, "CN=Administrators,OU=Groups,DC=example,DC=com", "Admin", 0));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("CN=Administrators,OU=Groups,DC=example,DC=com", result.GroupName);
        Assert.Equal("Admin", result.RoleName);
        Assert.Equal(0, result.Priority);
        Assert.Null(result.ProviderId);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenRepositoryReturnsNull()
    {
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.UpdateAsync(It.IsAny<RoleMapping>())).ReturnsAsync((RoleMapping?)null);

        var sender = CreateSender(repo.Object);

        var result = await sender.Send(new UpdateRoleMappingCommand(Guid.NewGuid(), null, "CN=Administrators,OU=Groups,DC=example,DC=com", "Admin", 0));

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsRepositoryResult()
    {
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.DeleteAsync(It.IsAny<Guid>())).ReturnsAsync(true);

        var sender = CreateSender(repo.Object);

        Assert.True(await sender.Send(new DeleteRoleMappingCommand(Guid.NewGuid())));
    }
}
