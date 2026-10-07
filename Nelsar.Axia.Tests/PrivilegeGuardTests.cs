using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Groups.Commands;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A permission says what kind of change a caller may make; this is about to whom. users.update —
/// the UserEditor role's, out of the box — used to be enough to reset an administrator's password
/// and sign in as them, users.delete to delete every administrator, groups.manage to join the
/// group mapped to Admin. Each target now has to hold nothing the caller lacks.
/// </summary>
public class PrivilegeGuardTests
{
    private const string EditorDn = "uid=editor,ou=people,dc=example,dc=com";
    private const string AdminDn = "uid=admin,ou=people,dc=example,dc=com";
    private const string PeerDn = "uid=peer,ou=people,dc=example,dc=com";
    private const string AdminGroupDn = "cn=Administrators,ou=groups,dc=example,dc=com";

    private static readonly IReadOnlySet<string> Editor = new HashSet<string>
    {
        Permissions.UsersRead, Permissions.UsersUpdate, Permissions.UsersDelete, Permissions.GroupsManage
    };

    private static readonly IReadOnlySet<string> Everything = Permissions.All.ToHashSet();

    private static Guid UserId(string login) => DeterministicIdFactory.BuildUserId($"{login}@example.com");

    private static (ISender Sender, Mock<ILdapService> Ldap) Pipeline(string callerDn = EditorDn)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(DirectoryUserListing.Complete(
        [
            new LdapUserInfo { Dn = AdminDn, Username = "admin", Email = "admin@example.com" },
            new LdapUserInfo { Dn = PeerDn, Username = "peer", Email = "peer@example.com" },
            new LdapUserInfo { Dn = EditorDn, Username = "editor", Email = "editor@example.com" }
        ]));
        ldap.Setup(x => x.GetGroupsAsync()).ReturnsAsync(DirectoryGroupListing.Complete(
        [
            new LdapGroup { Dn = AdminGroupDn, Name = "Administrators", Members = [AdminDn] }
        ]));
        ldap.Setup(x => x.DeleteUserAsync(It.IsAny<string>())).ReturnsAsync(true);
        ldap.Setup(x => x.DeleteGroupAsync(It.IsAny<string>())).ReturnsAsync(true);

        // What each entry would hold if it signed in: the administrator everything, a peer
        // only reading, the editor what the UserEditor role gives — plus groups.manage, so the
        // group cases are refused by this check rather than by the permission itself.
        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(EditorDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Editor);
        permissions.Setup(x => x.ResolveAsync(AdminDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Everything);
        permissions.Setup(x => x.ResolveForEntryAsync(EditorDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Editor);
        permissions.Setup(x => x.ResolveForEntryAsync(AdminDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Everything);
        permissions.Setup(x => x.ResolveForEntryAsync(PeerDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { Permissions.UsersRead });

        var mappings = new Mock<IRoleMappingRepository>();
        mappings.Setup(x => x.GetActiveForProviderAsync(It.IsAny<Guid?>()))
            .ReturnsAsync([RoleMapping.Create(null, AdminGroupDn, UserRoles.Admin, 0)]);

        var grants = new Mock<IRolePermissionRepository>();
        grants.Setup(x => x.GetPermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> roles, CancellationToken _) =>
                roles.Contains(UserRoles.Admin) ? Everything : new HashSet<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(permissions.Object);
        services.AddSingleton(mappings.Object);
        services.AddSingleton(grants.Object);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("caller@example.com", distinguishedName: callerDn));
        services.AddSingleton(new Mock<IUserLookupRepository>().Object);
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.WithAuditTrail();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), ldap);
    }

    [Fact]
    public async Task AnEditor_CannotResetAnAdministratorsPassword()
    {
        var (sender, ldap) = Pipeline();

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateUserCommand(UserId("admin"), "Admin", null, "taken-over")));

        ldap.Verify(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task AnEditor_CannotDeleteAnAdministrator()
    {
        var (sender, ldap) = Pipeline();

        await Assert.ThrowsAsync<PermissionDeniedException>(() => sender.Send(new DeleteUserCommand(UserId("admin"))));

        ldap.Verify(x => x.DeleteUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnEditor_StillEditsAccountsThatHoldNoMoreThanThey()
    {
        var (sender, ldap) = Pipeline();

        await sender.Send(new UpdateUserCommand(UserId("peer"), "Peer", null, "new-password"));
        await sender.Send(new UpdateUserCommand(UserId("editor"), "Editor", null, "own-password"));

        ldap.Verify(x => x.UpdateUserAsync(PeerDn, "Peer", null, "new-password"), Times.Once);
        ldap.Verify(x => x.UpdateUserAsync(EditorDn, "Editor", null, "own-password"), Times.Once);
    }

    [Fact]
    public async Task AnAdministrator_MayResetAnotherAdministratorsPassword()
    {
        var (sender, ldap) = Pipeline(callerDn: AdminDn);

        await sender.Send(new UpdateUserCommand(UserId("admin"), "Admin", null, "rotated"));

        ldap.Verify(x => x.UpdateUserAsync(AdminDn, "Admin", null, "rotated"), Times.Once);
    }

    [Fact]
    public async Task GroupsManage_CannotAddAnyoneToTheGroupMappedToAdmin()
    {
        var (sender, ldap) = Pipeline();
        var groupId = DeterministicIdFactory.BuildDirectoryObjectId(AdminGroupDn);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateGroupCommand(groupId, null, null, null, [AdminDn, EditorDn])));

        ldap.Verify(x => x.UpdateGroupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>?>()), Times.Never);
    }

    [Fact]
    public async Task GroupsManage_CannotDeleteTheGroupMappedToAdmin()
    {
        var (sender, ldap) = Pipeline();
        var groupId = DeterministicIdFactory.BuildDirectoryObjectId(AdminGroupDn);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => sender.Send(new DeleteGroupCommand(groupId)));

        ldap.Verify(x => x.DeleteGroupAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GroupsManage_CannotCreateAGroupUnderTheMappedGroupsName()
    {
        // Elsewhere in the tree, with the caller as its only member: the way a CN-only mapping
        // was beaten, and still a group that would be mistaken for the real one.
        var (sender, ldap) = Pipeline();

        await Assert.ThrowsAsync<PermissionDeniedException>(() => sender.Send(new CreateGroupCommand(
            "Administrators", "ou=sales,dc=example,dc=com", string.Empty, [EditorDn])));

        ldap.Verify(x => x.CreateGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task GroupsManage_StillCreatesGroupsNoMappingNames()
    {
        var (sender, ldap) = Pipeline();
        ldap.Setup(x => x.CreateGroupAsync("Helpdesk", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync("cn=Helpdesk,ou=groups,dc=example,dc=com");

        var created = await sender.Send(new CreateGroupCommand("Helpdesk", string.Empty, string.Empty, [EditorDn]));

        Assert.Equal("cn=Helpdesk,ou=groups,dc=example,dc=com", created.DistinguishedName);
    }
}
