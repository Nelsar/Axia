using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Roles.Commands;
using Nelsar.Axia.Application.Features.Roles.Queries;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The grants table — which permissions each role holds — used to be editable only by hand in the
/// database. These cover the API that edits it, and the rules that keep an edit from locking
/// everyone out or handing somebody more than the editor holds.
/// </summary>
public class RolePermissionsTests
{
    private static (ISender Sender, Mock<IRolePermissionRepository> Grants) Pipeline(
        IPermissionResolver permissions,
        params RolePermission[] rows)
    {
        var grants = new Mock<IRolePermissionRepository>();
        grants.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        grants.Setup(x => x.GetForRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string role, CancellationToken _) =>
                rows.Where(r => string.Equals(r.RoleName, role, StringComparison.OrdinalIgnoreCase)).ToList());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(grants.Object);
        services.AddSingleton(permissions);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("admin@example.com"));
        services.WithAuditTrail();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), grants);
    }

    private static (ISender Sender, Mock<IRolePermissionRepository> Grants) AsAdmin(params RolePermission[] rows) =>
        Pipeline(new FixedPermissionResolver([.. Permissions.All]), rows);

    [Fact]
    public async Task TheOverview_ListsEveryRole_InOrder_WithAdminNotEditable()
    {
        var (sender, _) = AsAdmin(
            RolePermission.Grant(UserRoles.UserEditor, Permissions.UsersUpdate),
            RolePermission.Grant(UserRoles.UserEditor, Permissions.UsersRead));

        var overview = await sender.Send(new GetRolesQuery());

        Assert.Equal(Permissions.All, overview.Permissions);
        Assert.Equal(UserRoles.All, overview.Roles.Select(r => r.RoleName));
        Assert.False(overview.Roles.Single(r => r.RoleName == UserRoles.Admin).IsEditable);
        Assert.All(overview.Roles.Where(r => r.RoleName != UserRoles.Admin), r => Assert.True(r.IsEditable));

        // Catalogue order, whatever order the rows came in.
        Assert.Equal(
            new[] { Permissions.UsersRead, Permissions.UsersUpdate },
            overview.Roles.Single(r => r.RoleName == UserRoles.UserEditor).Permissions);
    }

    [Fact]
    public async Task AnUpdate_ReplacesTheRolesGrants_UnderTheirCanonicalSpelling()
    {
        var (sender, grants) = AsAdmin(RolePermission.Grant(UserRoles.UserEditor, Permissions.UsersUpdate));

        var result = await sender.Send(new UpdateRolePermissionsCommand(
            "usereditor", ["USERS.READ", Permissions.UsersDelete, "users.read"]));

        grants.Verify(x => x.SetPermissionsForRoleAsync(
            UserRoles.UserEditor,
            It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { Permissions.UsersRead, Permissions.UsersDelete })),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(UserRoles.UserEditor, result.RoleName);
        Assert.Equal(new[] { Permissions.UsersRead, Permissions.UsersDelete }, result.Permissions);
    }

    [Fact]
    public async Task Admin_IsNotEdited_SoNobodyCanTakeRolesManageAwayFromIt()
    {
        var (sender, grants) = AsAdmin();

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new UpdateRolePermissionsCommand(UserRoles.Admin, [Permissions.UsersRead])));

        grants.Verify(x => x.SetPermissionsForRoleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RolesManage_IsNotGrantedToAnyOtherRole_BecauseItIsAdminUnderAnotherName()
    {
        var (sender, grants) = AsAdmin();

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new UpdateRolePermissionsCommand(UserRoles.User, [Permissions.RolesManage])));

        grants.Verify(x => x.SetPermissionsForRoleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownRole_IsNotFound_AndAnUnknownPermission_IsRefused()
    {
        var (sender, _) = AsAdmin();

        await Assert.ThrowsAsync<DomainNotFoundException>(
            () => sender.Send(new UpdateRolePermissionsCommand("Auditors", [Permissions.AuditRead])));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new UpdateRolePermissionsCommand(UserRoles.User, ["users.everything"])));
    }

    [Fact]
    public async Task NobodyGrantsAPermissionTheyDoNotHold_ButMayTakeOneAway()
    {
        // roles.manage is Admin's alone today, so this holds trivially for the default grants. It is
        // the same subset rule PrivilegeGuard applies, kept here so that stays true if it is not.
        var limited = new FixedPermissionResolver(Permissions.RolesManage, Permissions.UsersRead);
        var (sender, grants) = Pipeline(limited, RolePermission.Grant(UserRoles.UserDeleter, Permissions.UsersDelete));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateRolePermissionsCommand(UserRoles.UserDeleter, [Permissions.UsersDelete, Permissions.AuditRead])));

        // Keeping users.delete (already there) and dropping nothing it lacks is fine, as is removal.
        await sender.Send(new UpdateRolePermissionsCommand(UserRoles.UserDeleter, [Permissions.UsersRead]));
        grants.Verify(x => x.SetPermissionsForRoleAsync(UserRoles.UserDeleter,
            It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { Permissions.UsersRead })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EditingGrants_NeedsRolesManage()
    {
        var (sender, _) = Pipeline(new FixedPermissionResolver(Permissions.UsersRead));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => sender.Send(new GetRolesQuery()));
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateRolePermissionsCommand(UserRoles.User, [Permissions.UsersRead])));
    }

    [Fact]
    public async Task MyPermissions_AreWhatTheResolverSays_InCatalogueOrder()
    {
        var (sender, _) = Pipeline(new FixedPermissionResolver(Permissions.UsersDelete, Permissions.UsersRead, Permissions.UsersUpdate));

        var mine = await sender.Send(new GetMyPermissionsQuery());

        Assert.Equal(new[] { Permissions.UsersRead, Permissions.UsersUpdate, Permissions.UsersDelete }, mine);
    }

    [Fact]
    public async Task MyPermissions_AreRefused_WithNobodySignedIn()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton<IPermissionResolver>(new FixedPermissionResolver([.. Permissions.All]));
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(subject: null));
        services.WithAuditTrail();
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        await Assert.ThrowsAsync<NotAuthenticatedException>(() => sender.Send(new GetMyPermissionsQuery()));
    }

    [Fact]
    public async Task TheEndpoints_AnswerOverHttp()
    {
        using var factory = new ApiTestFactory(_ => { });
        var client = factory.CreateClient();

        // Asking what one may do is not a sign-in: it must not need a token-less path, nor share
        // the sign-in rate limit.
        var anonymous = await client.GetAsync("/api/v1/me/permissions");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.User));

        for (var i = 0; i < 8; i++)
        {
            var mine = await client.GetAsync("/api/v1/me/permissions");
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        }

        var permissions = await client.GetFromJsonAsync<string[]>("/api/v1/me/permissions");
        Assert.Equal(Permissions.All, permissions);

        var update = await client.PutAsJsonAsync(
            "/api/v1/roles/UserEditor/permissions",
            new RolePermissionsUpdateRequest { Permissions = [Permissions.UsersRead, Permissions.UsersUpdate] });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var overview = await client.GetFromJsonAsync<RolesOverviewDto>("/api/v1/roles");
        Assert.NotNull(overview);
        Assert.Equal(
            new[] { Permissions.UsersRead, Permissions.UsersUpdate },
            overview.Roles.Single(r => r.RoleName == UserRoles.UserEditor).Permissions);

        var admin = await client.PutAsJsonAsync(
            "/api/v1/roles/Admin/permissions", new RolePermissionsUpdateRequest { Permissions = [] });
        Assert.Equal(HttpStatusCode.BadRequest, admin.StatusCode);

        var unknown = await client.PutAsJsonAsync(
            "/api/v1/roles/Auditors/permissions", new RolePermissionsUpdateRequest { Permissions = [] });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
