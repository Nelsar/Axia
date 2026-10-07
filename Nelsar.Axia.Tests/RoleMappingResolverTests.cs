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
    private const string Administrators = "CN=Administrators,OU=Groups,DC=example,DC=com";
    private const string UserCreators = "CN=UserCreators,OU=Groups,DC=example,DC=com";

    private static RoleMappingResolver ResolverOver(Guid? providerId, params RoleMapping[] mappings)
    {
        var repo = new Mock<IRoleMappingRepository>();
        repo.Setup(x => x.GetActiveForProviderAsync(providerId)).ReturnsAsync(mappings);
        return new RoleMappingResolver(repo.Object);
    }

    [Fact]
    public async Task ResolveRoleAsync_ReturnsUser_WhenNoGroupsMatch()
    {
        var resolver = ResolverOver(null, RoleMapping.Create(null, Administrators, UserRoles.Admin, 0));

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { "CN=SomeOtherGroup,OU=Groups,DC=example,DC=com" }, null);

        Assert.Equal(UserRoles.User, role);
    }

    [Fact]
    public async Task ResolveRoleAsync_PicksHighestPriority_WhenUserInMultipleGroups()
    {
        var resolver = ResolverOver(null,
            RoleMapping.Create(null, Administrators, UserRoles.Admin, 0),
            RoleMapping.Create(null, UserCreators, UserRoles.UserCreator, 10));

        // User is in both groups - Priority 0 (Admin) must win regardless of array order.
        var role = await resolver.ResolvePrimaryRoleAsync(new[] { UserCreators, Administrators }, null);

        Assert.Equal(UserRoles.Admin, role);
    }

    [Fact]
    public async Task ResolveRoleAsync_ProviderSpecificMapping_TakesPrecedenceOverGlobal()
    {
        const string itStaff = "CN=IT-Staff,OU=Groups,DC=example,DC=com";
        var providerId = Guid.NewGuid();
        var resolver = ResolverOver(providerId,
            // Global mapping says this group is just a User; the domain-specific
            // override says it's an Admin for this particular LDAP domain.
            RoleMapping.Create(null, itStaff, UserRoles.User, 100),
            RoleMapping.Create(providerId, itStaff, UserRoles.Admin, 50));

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { itStaff }, providerId);

        Assert.Equal(UserRoles.Admin, role);
    }

    [Fact]
    public async Task ADnMapping_IgnoresCaseAndSpacingBetweenComponents()
    {
        // As an administrator types it, against the way a directory returns it.
        var resolver = ResolverOver(null,
            RoleMapping.Create(null, "CN=Administrators, OU=Groups, DC=example, DC=com", UserRoles.Admin, 0));

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { "cn=administrators,ou=groups,dc=example,dc=com" }, null);

        Assert.Equal(UserRoles.Admin, role);
    }

    [Fact]
    public async Task ADnMapping_DoesNotGrantItsRole_ToAnotherGroupWithTheSameName()
    {
        // The hole a CN-only mapping left: whoever can create a group anywhere could create one
        // called Administrators, join it, and be an administrator. A DN names one entry.
        var resolver = ResolverOver(null, RoleMapping.Create(null, Administrators, UserRoles.Admin, 0));

        var roles = await resolver.ResolveRolesAsync(
            new[] { "CN=Administrators,OU=Sales,OU=Delegated,DC=example,DC=com" }, null);

        Assert.Equal(new[] { UserRoles.User }, roles);
    }

    [Fact]
    public async Task AMappingStillWrittenByName_KeepsMatchingByCn_SoNobodyIsLockedOutOnUpgrade()
    {
        var legacy = RoleMapping.SeedByGroupName(null, "Administrators", UserRoles.Admin, 0);
        var resolver = ResolverOver(null, legacy);

        var role = await resolver.ResolvePrimaryRoleAsync(new[] { "cn=administrators,OU=Groups,DC=example,DC=com" }, null);

        Assert.Equal(UserRoles.Admin, role);
        Assert.True(legacy.IdentifiesGroupByNameOnly);
    }

    [Fact]
    public async Task AGroupNameContainingAnEscapedComma_IsReadWhole()
    {
        // "CN=Admins\, Tier 0,…" used to be read as a group called "Admins\" — a name that is not
        // the group's, and could be some other group's.
        var resolver = ResolverOver(null, RoleMapping.SeedByGroupName(null, "Admins, Tier 0", UserRoles.Admin, 0));

        var roles = await resolver.ResolveRolesAsync(new[] { @"CN=Admins\, Tier 0,OU=Groups,DC=example,DC=com" }, null);

        Assert.Equal(new[] { UserRoles.Admin }, roles);
    }

    [Fact]
    public async Task ResolveGrantedRoles_IsEmpty_ForAnUnmappedGroup_RatherThanTheUserFallback()
    {
        // What a group confers is a question about the group; the User fallback is about people.
        var resolver = ResolverOver(null, RoleMapping.Create(null, Administrators, UserRoles.Admin, 0));

        var roles = await resolver.ResolveGrantedRolesAsync(new[] { UserCreators }, null);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task ResolveRolesForGroupName_FindsMappingsByNameInEitherForm()
    {
        var resolver = ResolverOver(null,
            RoleMapping.Create(null, Administrators, UserRoles.Admin, 0),
            RoleMapping.SeedByGroupName(null, "UserCreators", UserRoles.UserCreator, 10));

        Assert.Equal(new[] { UserRoles.Admin }, await resolver.ResolveRolesForGroupNameAsync("administrators", null));
        Assert.Equal(new[] { UserRoles.UserCreator }, await resolver.ResolveRolesForGroupNameAsync("UserCreators", null));
        Assert.Empty(await resolver.ResolveRolesForGroupNameAsync("Helpdesk", null));
    }
}
