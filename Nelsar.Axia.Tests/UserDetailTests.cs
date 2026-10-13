using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// What the console's detail view shows of one account: its login, its DN and its groups.
///
/// The view showed "—" for the login and the DN of every row opened from a search, and "no
/// membership" for accounts that were members of groups — in a directory that keeps no memberOf,
/// for every account.
/// </summary>
public class UserDetailTests
{
    private const string Engineering = "cn=engineering,ou=groups,dc=example,dc=org";

    // An OpenLDAP entry without the memberof overlay: nothing on the entry says which groups
    // it is in. Only the group does, in its member attribute.
    private static readonly LdapUserInfo Ivan = new()
    {
        Dn = "uid=ivan.petrov,ou=staff,ou=people,dc=example,dc=org",
        Username = "ivan.petrov",
        DisplayName = "Ivan Petrov",
        Email = "ivan.petrov@example.org",
        Groups = []
    };

    [Fact]
    public async Task OneAccount_ShowsTheGroupsThatListIt_EvenWhenTheListingCachedItFirst()
    {
        var (sender, ldap) = Pipeline();
        // Read by DN, the entry comes back as sign-in reads it: with the groups that name it.
        ldap.Setup(x => x.GetUserByDnAsync(Ivan.Dn)).ReturnsAsync(new LdapUserInfo
        {
            Dn = Ivan.Dn,
            Username = Ivan.Username,
            DisplayName = Ivan.DisplayName,
            Email = Ivan.Email,
            Groups = [Engineering]
        });

        // The full listing reads memberOf alone and caches what it read for half an hour; the
        // dashboard and the users screen both ask for it before anyone opens an account.
        var listing = await sender.Send(new GetAllUsersQuery());
        var user = await sender.Send(new GetUserByIdQuery(Assert.Single(listing.Items).Id));

        Assert.Equal([Engineering], user!.Groups);
    }

    [Fact]
    public async Task ASearchResult_CarriesItsLoginAndDn()
    {
        var (sender, _) = Pipeline();

        var page = await sender.Send(new SearchUsersQuery("ivan", 50, null));

        var row = Assert.Single(page.Items);
        Assert.Equal("ivan.petrov", row.Username);
        Assert.Equal(Ivan.Dn, row.Dn);
    }

    private static (ISender Sender, Mock<ILdapService> Ldap) Pipeline()
    {
        var ldap = new Mock<ILdapService>();
        var listing = new DirectoryUserListing([Ivan], IsComplete: true);
        ldap.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(listing);
        ldap.Setup(x => x.SearchUsersAsync(It.IsAny<string>())).ReturnsAsync(listing);
        ldap.Setup(x => x.GetGroupMembersAsync(It.IsAny<string>())).ReturnsAsync(DirectoryGroupMembers.None);

        var mappings = new Mock<IRoleMappingRepository>();
        mappings.Setup(x => x.GetActiveForProviderAsync(It.IsAny<Guid?>())).ReturnsAsync([]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(mappings.Object);
        services.AddSingleton(new Mock<IUserLookupRepository>().Object);
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.WithAuthorizedCaller();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), ldap);
    }
}
