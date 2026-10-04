using Microsoft.AspNetCore.Http;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Application.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;

namespace Nelsar.Axia.Tests;

public class LdapServiceTests
{
    [Fact]
    public async Task GetUserRoleAsync_PassesGroupsAndNoProviderId_WhenHeaderNotSet()
    {
        var provider = new Mock<ILdapProvider>();
        provider.Setup(x => x.GetUserGroups("alice")).Returns(new[] { "CN=Administrators,OU=Groups,DC=example,DC=com" });

        var providerFactory = new Mock<ILdapProviderFactory>();
        providerFactory.Setup(x => x.Resolve()).Returns(provider.Object);

        var resolver = new Mock<IRoleMappingResolver>();
        resolver.Setup(x => x.ResolvePrimaryRoleAsync(It.IsAny<IEnumerable<string>>(), null))
            .ReturnsAsync(UserRoles.Admin);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var service = new LdapService(providerFactory.Object, resolver.Object, httpContextAccessor.Object);

        var role = await service.GetUserRoleAsync("alice");

        Assert.Equal(UserRoles.Admin, role);
        resolver.Verify(x => x.ResolvePrimaryRoleAsync(It.Is<IEnumerable<string>>(g => g.Contains("CN=Administrators,OU=Groups,DC=example,DC=com")), null), Times.Once);
    }

    [Fact]
    public async Task GetUserRoleAsync_ExtractsProviderIdFromHttpContextItems()
    {
        var providerId = Guid.NewGuid();

        var provider = new Mock<ILdapProvider>();
        provider.Setup(x => x.GetUserGroups("bob")).Returns(Array.Empty<string>());

        var providerFactory = new Mock<ILdapProviderFactory>();
        providerFactory.Setup(x => x.Resolve()).Returns(provider.Object);

        var resolver = new Mock<IRoleMappingResolver>();
        resolver.Setup(x => x.ResolvePrimaryRoleAsync(It.IsAny<IEnumerable<string>>(), providerId))
            .ReturnsAsync(UserRoles.UserEditor);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["LdapProviderId"] = providerId.ToString();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new LdapService(providerFactory.Object, resolver.Object, httpContextAccessor.Object);

        var role = await service.GetUserRoleAsync("bob");

        Assert.Equal(UserRoles.UserEditor, role);
        resolver.Verify(x => x.ResolvePrimaryRoleAsync(It.IsAny<IEnumerable<string>>(), providerId), Times.Once);
    }
}
