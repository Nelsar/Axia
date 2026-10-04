using Moq;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;

namespace Nelsar.Axia.Tests;

public class RoleMappingResolverTests
{
    [Fact]
    public async Task ResolveRoleAsync_ReturnsUser_WhenNoGroupsMatch()
    {
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.GetActiveForProviderAsync(null))
            .ReturnsAsync(new[]
            {
                RoleMapping.Create(null, "Administrators", UserRoles.Admin, 0)
            });

        var resolver = new RoleMappingResolver(repo.Object);

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { "CN=SomeOtherGroup,OU=Groups,DC=example,DC=com" }, null);

        Assert.Equal(UserRoles.User, role);
    }

    [Fact]
    public async Task ResolveRoleAsync_PicksHighestPriority_WhenUserInMultipleGroups()
    {
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.GetActiveForProviderAsync(null))
            .ReturnsAsync(new[]
            {
                RoleMapping.Create(null, "Administrators", UserRoles.Admin, 0),
                RoleMapping.Create(null, "UserCreators", UserRoles.UserCreator, 10)
            });

        var resolver = new RoleMappingResolver(repo.Object);

        // User is in both groups - Priority 0 (Admin) must win regardless of array order.
        var role = await resolver.ResolvePrimaryRoleAsync(new[]
        {
            "CN=UserCreators,OU=Groups,DC=example,DC=com",
            "CN=Administrators,OU=Groups,DC=example,DC=com"
        }, null);

        Assert.Equal(UserRoles.Admin, role);
    }

    [Fact]
    public async Task ResolveRoleAsync_ProviderSpecificMapping_TakesPrecedenceOverGlobal()
    {
        var providerId = Guid.NewGuid();
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.GetActiveForProviderAsync(providerId))
            .ReturnsAsync(new[]
            {
                // Global mapping says this group is just a User; the domain-specific
                // override says it's an Admin for this particular LDAP domain.
                RoleMapping.Create(null, "IT-Staff", UserRoles.User, 100),
                RoleMapping.Create(providerId, "IT-Staff", UserRoles.Admin, 50)
            });

        var resolver = new RoleMappingResolver(repo.Object);

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { "CN=IT-Staff,OU=Groups,DC=example,DC=com" }, providerId);

        Assert.Equal(UserRoles.Admin, role);
    }

    [Fact]
    public async Task ResolveRoleAsync_IsCaseInsensitiveOnGroupName()
    {
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.GetActiveForProviderAsync(null))
            .ReturnsAsync(new[]
            {
                RoleMapping.Create(null, "Administrators", UserRoles.Admin, 0)
            });

        var resolver = new RoleMappingResolver(repo.Object);

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { "cn=administrators,OU=Groups,DC=example,DC=com" }, null);

        Assert.Equal(UserRoles.Admin, role);
    }
}
