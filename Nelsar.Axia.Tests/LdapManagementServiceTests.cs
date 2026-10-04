using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Nelsar.Axia.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Xunit;

namespace Nelsar.Axia.Tests;

public class LdapManagementServiceTests
{
    private static ISender CreateSender(Mock<ILdapService> ldap)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();
        services.AddSingleton(ldap.Object);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task SearchUsersAsync_ShouldReturnPagedUsers_WhenPageSizeIsSmallerThanTotal()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(new[]
            {
                new LdapUserInfo
                {
                    Email = "alice@example.com",
                    DisplayName = "Alice"
                },
                new LdapUserInfo
                {
                    Email = "bob@example.com",
                    DisplayName = "Bob"
                },
                new LdapUserInfo
                {
                    Email = "carol@example.com",
                    DisplayName = "Carol"
                }
            }));

        var sender = CreateSender(ldapServiceMock);

        var result = await sender.Send(new SearchUsersQuery(null, 2, null));

        Assert.Equal(2, result.Items.Count());
        Assert.Equal(2, result.PageSize);
        Assert.Equal("2", result.PageToken);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task SearchUsersAsync_ShouldFilterUsersByNameOrEmail()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.SearchUsersAsync(It.Is<string>(filter => filter.Contains("wonder", StringComparison.OrdinalIgnoreCase))))
            .ReturnsAsync(DirectoryUserListing.Complete(new[]
            {
                new LdapUserInfo
                {
                    Email = "alice@example.com",
                    DisplayName = "Alice Wonderland"
                }
            }));

        var sender = CreateSender(ldapServiceMock);

        var result = await sender.Send(new SearchUsersQuery("wonder", 10, null));

        Assert.Single(result.Items);
        Assert.Contains(result.Items, user => user.Email == "alice@example.com");
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task SearchUsersAsync_ShouldBuildSafeLdapFilter_WhenFilterIsProvided()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.SearchUsersAsync(It.Is<string>(filter =>
            filter.Contains("(&(objectClass=user)(objectCategory=person)(|") &&
            filter.Contains("(mail=*Alice*)") &&
            filter.Contains("(displayName=*Alice*)") &&
            filter.Contains("(uid=*Alice*)") &&
            filter.Contains("(sAMAccountName=*Alice*)"))))
            .ReturnsAsync(DirectoryUserListing.Complete(
            [
                new LdapUserInfo
                {
                    Email = "alice@example.com",
                    DisplayName = "Alice"
                }
            ]));

        var sender = CreateSender(ldapServiceMock);

        var result = await sender.Send(new SearchUsersQuery("Alice", 10, null));

        Assert.Single(result.Items);
        Assert.Equal("alice@example.com", result.Items.Single().Email);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task SearchUsersAsync_MarksThePageIncomplete_WhenTheEnumerationWasCutShort()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Truncated(
            [
                new LdapUserInfo { Email = "alice@example.com", DisplayName = "Alice" }
            ]));

        var sender = CreateSender(ldapServiceMock);

        var result = await sender.Send(new SearchUsersQuery(null, 10, null));

        Assert.Single(result.Items);
        // Paging over a partial set produces page counts that do not describe the directory.
        Assert.False(result.IsComplete);
    }
}
