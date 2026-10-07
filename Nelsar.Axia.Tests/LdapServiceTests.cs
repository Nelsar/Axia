using Microsoft.AspNetCore.Http;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Application.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;

namespace Nelsar.Axia.Tests;

public class LdapServiceTests
{
    [Fact]
    public async Task GetUserRoleAsync_PassesGroupsAndNoProviderId_WhenHeaderNotSet()
    {
        var provider = new Mock<ILdapProvider>(MockBehavior.Strict);

        var providerFactory = new Mock<ILdapProviderFactory>();
        providerFactory.Setup(x => x.Resolve()).Returns(provider.Object);

        var resolver = new Mock<IRoleMappingResolver>();
        resolver.Setup(x => x.ResolvePrimaryRoleAsync(It.IsAny<IEnumerable<string>>(), null))
            .ReturnsAsync(UserRoles.Admin);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var service = new LdapService(providerFactory.Object, resolver.Object, httpContextAccessor.Object);

        // The groups are the entry's own, as read at sign-in. Strict: nothing is looked up again
        // by login, which could find a different entry.
        var role = await service.GetUserRoleAsync(new LdapUserInfo
        {
            Dn = "CN=alice,OU=Users,DC=example,DC=com",
            Groups = ["CN=Administrators,OU=Groups,DC=example,DC=com"]
        });

        Assert.Equal(UserRoles.Admin, role);
        resolver.Verify(x => x.ResolvePrimaryRoleAsync(It.Is<IEnumerable<string>>(g => g.Contains("CN=Administrators,OU=Groups,DC=example,DC=com")), null), Times.Once);
    }

    [Fact]
    public async Task GetUserRoleAsync_ExtractsProviderIdFromHttpContextItems()
    {
        var providerId = Guid.NewGuid();

        var providerFactory = new Mock<ILdapProviderFactory>();
        providerFactory.Setup(x => x.Resolve()).Returns(new Mock<ILdapProvider>().Object);

        var resolver = new Mock<IRoleMappingResolver>();
        resolver.Setup(x => x.ResolvePrimaryRoleAsync(It.IsAny<IEnumerable<string>>(), providerId))
            .ReturnsAsync(UserRoles.UserEditor);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["LdapProviderId"] = providerId.ToString();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new LdapService(providerFactory.Object, resolver.Object, httpContextAccessor.Object);

        var role = await service.GetUserRoleAsync(new LdapUserInfo { Dn = "CN=bob,OU=Users,DC=example,DC=com" });

        Assert.Equal(UserRoles.UserEditor, role);
        resolver.Verify(x => x.ResolvePrimaryRoleAsync(It.IsAny<IEnumerable<string>>(), providerId), Times.Once);
    }

    [Fact]
    public async Task GetUserByDnAsync_ReadsTheEntryAtThatDn()
    {
        const string dn = "CN=alice,OU=Users,DC=example,DC=com";
        var entry = new LdapUserInfo { Dn = dn, Username = "alice" };

        var provider = new Mock<ILdapProvider>(MockBehavior.Strict);
        provider.Setup(x => x.FindUserByDn(dn)).Returns(entry);

        var providerFactory = new Mock<ILdapProviderFactory>();
        providerFactory.Setup(x => x.Resolve()).Returns(provider.Object);

        var service = new LdapService(
            providerFactory.Object, new Mock<IRoleMappingResolver>().Object, new Mock<IHttpContextAccessor>().Object);

        Assert.Same(entry, await service.GetUserByDnAsync(dn));
    }
}
