using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The role column of the user list: the role each account would sign in with.
///
/// Search and the full listing set it to User for every account, administrators included; only
/// sign-in ever worked it out. It now comes from the role mappings, against each account's
/// memberOf and the members of the mapped groups — so it is right where the directory keeps no
/// memberOf as well.
/// </summary>
public class UserListRoleTests
{
    private const string Admins = "cn=axia-admins,ou=groups,dc=example,dc=org";
    private const string Creators = "cn=axia-creators,ou=groups,dc=example,dc=org";
    private const string Editors = "cn=axia-editors,ou=groups,dc=example,dc=org";

    // Admin through memberOf, as Active Directory or an overlay reports it.
    private static readonly LdapUserInfo Admin = Entry("admin.demo", groups: [Admins]);

    // UserEditor through the group's member list only: no memberOf at all.
    private static readonly LdapUserInfo Editor = Entry("editor.demo");

    // UserCreator through a posixGroup's memberUid, which names the login, not the DN.
    private static readonly LdapUserInfo Creator = Entry("creator.demo");

    private static readonly LdapUserInfo Plain = Entry("user.demo");

    [Fact]
    public async Task Search_ShowsTheRoleEachAccountSignsInWith()
    {
        var (sender, _) = Pipeline();

        var page = await sender.Send(new SearchUsersQuery(null, 50, null));

        Assert.Equal(["Admin", "UserEditor", "UserCreator", "User"], page.Items.Select(u => u.Role));
    }

    [Fact]
    public async Task TheFullListing_ShowsThemToo()
    {
        var (sender, _) = Pipeline();

        var listing = await sender.Send(new GetAllUsersQuery());

        Assert.Equal(["Admin", "UserEditor", "UserCreator", "User"], listing.Items.Select(u => u.Role));
    }

    [Fact]
    public async Task OneAccountReadById_ShowsItsRole()
    {
        // The detail view: an id the lookup index knows, resolved without walking the directory.
        var (sender, ldap, lookup) = PipelineWithLookup();
        var id = DeterministicIdFactory.BuildUserId(Editor.Email, Editor.Dn, Editor.Username);
        lookup.Setup(x => x.FindEmailAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Editor.Email);
        ldap.Setup(x => x.GetUserDetailsAsync(Editor.Email)).ReturnsAsync(Editor);

        var user = await sender.Send(new GetUserByIdQuery(id));

        Assert.Equal("UserEditor", user!.Role);
    }

    [Fact]
    public async Task EachMappedGroup_IsReadOnce_HoweverManyAccountsAreListed()
    {
        // The point of resolving a listing in one go: a directory read per account would be
        // thousands of them.
        var many = Enumerable.Range(0, 150).Select(i => Entry($"staff{i}")).Append(Editor).ToArray();
        var (sender, ldap) = Pipeline(many);

        var page = await sender.Send(new SearchUsersQuery(null, 200, null));

        Assert.Equal("UserEditor", page.Items.Last().Role);
        ldap.Verify(x => x.GetGroupMembersAsync(It.IsAny<string>()), Times.Exactly(3));
    }

    [Fact]
    public async Task AGroupThatCannotBeRead_CostsItsLabel_NotTheListing()
    {
        var (sender, ldap) = Pipeline();
        ldap.Setup(x => x.GetGroupMembersAsync(Editors))
            .ThrowsAsync(new DirectoryUnavailableException("the directory could not be read"));

        var page = await sender.Send(new SearchUsersQuery(null, 50, null));

        // memberOf still counts; only the role that needed the unreadable group falls back.
        Assert.Equal(["Admin", "User", "UserCreator", "User"], page.Items.Select(u => u.Role));
    }

    private static (ISender Sender, Mock<ILdapService> Ldap) Pipeline(params LdapUserInfo[] entries)
    {
        var (sender, ldap, _) = PipelineWithLookup(entries);
        return (sender, ldap);
    }

    private static (ISender Sender, Mock<ILdapService> Ldap, Mock<IUserLookupRepository> Lookup) PipelineWithLookup(
        params LdapUserInfo[] entries)
    {
        if (entries.Length == 0)
            entries = [Admin, Editor, Creator, Plain];

        var ldap = new Mock<ILdapService>();
        var listing = new DirectoryUserListing(entries, IsComplete: true);
        ldap.Setup(x => x.SearchUsersAsync(It.IsAny<string>())).ReturnsAsync(listing);
        ldap.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(listing);
        ldap.Setup(x => x.GetGroupMembersAsync(Admins))
            .ReturnsAsync(new DirectoryGroupMembers([Admin.Dn], []));
        ldap.Setup(x => x.GetGroupMembersAsync(Editors))
            // Spelled differently from the entry's own DN, as a directory may.
            .ReturnsAsync(new DirectoryGroupMembers(["UID=editor.demo, OU=people, DC=example, DC=org"], []));
        ldap.Setup(x => x.GetGroupMembersAsync(Creators))
            .ReturnsAsync(new DirectoryGroupMembers([], ["creator.demo"]));

        var mappings = new Mock<IRoleMappingRepository>();
        mappings.Setup(x => x.GetActiveForProviderAsync(It.IsAny<Guid?>())).ReturnsAsync(
        [
            RoleMapping.Create(null, Admins, UserRoles.Admin, 0),
            RoleMapping.Create(null, Creators, UserRoles.UserCreator, 10),
            RoleMapping.Create(null, Editors, UserRoles.UserEditor, 10)
        ]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(mappings.Object);
        var lookup = new Mock<IUserLookupRepository>();
        services.AddSingleton(lookup.Object);
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.WithAuthorizedCaller();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), ldap, lookup);
    }

    private static LdapUserInfo Entry(string login, string[]? groups = null) => new()
    {
        Dn = $"uid={login},ou=people,dc=example,dc=org",
        Username = login,
        DisplayName = login,
        Email = $"{login}@example.org",
        Groups = groups ?? []
    };
}
