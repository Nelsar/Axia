using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Features.Users.Commands;
using MediatR;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The whole chain, for real: LDAP group membership -> RoleMappings -> RolePermissions ->
/// [Authorize(Policy)]. Everything is resolved during the request; nothing is read from the
/// token, which still carries a role claim that authorisation deliberately ignores.
/// </summary>
public class PermissionAuthorizationTests
{
    private static readonly Guid AliceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string GroupDn(string name) => $"CN={name},OU=Groups,DC=example,DC=com";

    private static ApiTestFactory FactoryForMemberOf(params string[] groupNames)
    {
        // Whichever entry the token names, it is read at that DN and found in these groups.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserByDnAsync(It.IsAny<string>()))
            .ReturnsAsync((string dn) => new LdapUserInfo { Dn = dn, Groups = [.. groupNames.Select(GroupDn)] });

        var update = new Mock<IRequestHandler<UpdateUserCommand, UserDto?>>();
        update.Setup(x => x.Handle(It.IsAny<UpdateUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { Id = AliceId, Email = "alice@example.com", Name = "Alice" });
        var delete = new Mock<IRequestHandler<DeleteUserCommand, bool>>();
        delete.Setup(x => x.Handle(It.IsAny<DeleteUserCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var create = new Mock<IRequestHandler<CreateUserCommand, UserDto>>();
        create.Setup(x => x.Handle(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { Id = AliceId, Email = "alice@example.com", Name = "Alice" });

        var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IRequestHandler<UpdateUserCommand, UserDto?>));
            services.AddSingleton(update.Object);
            services.RemoveAll(typeof(IRequestHandler<DeleteUserCommand, bool>));
            services.AddSingleton(delete.Object);
            services.RemoveAll(typeof(IRequestHandler<CreateUserCommand, UserDto>));
            services.AddSingleton(create.Object);

            // The real thing, in place of the permissive default.
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });

        Seed(factory);
        return factory;
    }

    /// <summary>Mirrors what DbInitializer seeds; the Testing environment skips it.</summary>
    private static void Seed(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // By DN, as a mapping is written now — the seeder's by-name rows are covered in
        // RoleMappingResolverTests.
        db.RoleMappings.AddRange(
            RoleMapping.Create(null, GroupDn("UserEditors"), UserRoles.UserEditor, 0),
            RoleMapping.Create(null, GroupDn("UserDeleters"), UserRoles.UserDeleter, 0),
            RoleMapping.Create(null, GroupDn("UserCreators"), UserRoles.UserCreator, 0));

        void Grant(string role, params string[] permissions)
        {
            foreach (var permission in permissions)
                db.RolePermissions.Add(RolePermission.Grant(role, permission));
        }

        Grant(UserRoles.User, Permissions.UsersRead);
        Grant(UserRoles.UserEditor, Permissions.UsersRead, Permissions.UsersUpdate);
        Grant(UserRoles.UserDeleter, Permissions.UsersRead, Permissions.UsersDelete);
        Grant(UserRoles.UserCreator, Permissions.UsersRead, Permissions.UsersCreate);

        db.SaveChanges();
    }

    private static HttpClient SignedIn(ApiTestFactory factory, string email, string? distinguishedName = null,
        string? preferredUsername = null, bool includeDistinguishedName = true)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            // The role in the token is intentionally the weakest one available. Authorisation
            // must reach its verdict from directory membership regardless of what is claimed here.
            TestAuth.GenerateToken(UserRoles.User, email, preferredUsername: preferredUsername,
                distinguishedName: distinguishedName, includeDistinguishedName: includeDistinguishedName));
        return client;
    }

    [Fact]
    public async Task MembershipGrantsExactlyTheMappedPermission()
    {
        using var factory = FactoryForMemberOf("UserEditors");
        var client = SignedIn(factory, "editor@example.com");

        var update = await client.PutAsJsonAsync($"/api/v1/users/{AliceId}", new UserUpdateRequest { Name = "Alice" });
        var delete = await client.DeleteAsync($"/api/v1/users/{AliceId}");

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task TwoGroupsGrantTheUnionOfTheirPermissions()
    {
        using var factory = FactoryForMemberOf("UserEditors", "UserDeleters");
        var client = SignedIn(factory, "editor-and-deleter@example.com");

        var update = await client.PutAsJsonAsync($"/api/v1/users/{AliceId}", new UserUpdateRequest { Name = "Alice" });
        var delete = await client.DeleteAsync($"/api/v1/users/{AliceId}");

        // The headline change. Resolution used to pick a single winning role by priority, so one
        // of these two would have been refused however the administrator configured the groups.
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task NoMappedGroup_LeavesOnlyWhatTheUserRoleHolds()
    {
        using var factory = FactoryForMemberOf("SomeUnrelatedGroup");
        var client = SignedIn(factory, "nobody@example.com");

        var read = await client.GetAsync($"/api/v1/users/{AliceId}");
        var delete = await client.DeleteAsync($"/api/v1/users/{AliceId}");

        Assert.NotEqual(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task AdministrativeEndpoint_IsRefusedWithoutItsPermission()
    {
        using var factory = FactoryForMemberOf("UserEditors");
        var client = SignedIn(factory, "editor-only@example.com");

        var response = await client.PostAsJsonAsync("/api/v1/users", new UserCreateRequest
        {
            Email = "newuser@example.com",
            Name = "New User",
            Password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DirectoryOutageDuringAuthorisation_Fails502_NotForbidden()
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserByDnAsync(It.IsAny<string>()))
            .ThrowsAsync(new DirectoryUnavailableException("The directory could not be reached."));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });

        var client = SignedIn(factory, "anyone@example.com");
        var response = await client.GetAsync($"/api/v1/users/{AliceId}");

        // 403 would tell the user they lack a right they may well hold, and would hide the
        // outage. Membership unknown means the question cannot be answered, not answered "no".
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task PermissionsComeFromTheEntryTheTokenNames_NotFromWhoeverAnswersToItsLogin()
    {
        // The escalation this closes: an editor sets their own mail to an administrator's, signs
        // in with their own password, and is issued a token whose preferred_username is the
        // administrator's address. Permissions used to be resolved by searching for that
        // address, and the directory could answer with the administrator's entry.
        const string adminDn = "CN=Admin,OU=Users,DC=example,DC=com";
        const string editorDn = "CN=Editor,OU=Users,DC=example,DC=com";
        var readAt = new List<string>();

        var ldap = new Mock<ILdapService>(MockBehavior.Strict);
        ldap.Setup(x => x.GetUserByDnAsync(It.IsAny<string>()))
            .Callback<string>(readAt.Add)
            .ReturnsAsync((string dn) => new LdapUserInfo
            {
                Dn = dn,
                Email = "admin@example.com",
                Groups = dn == adminDn ? [GroupDn("UserDeleters")] : [GroupDn("UserEditors")]
            });

        var delete = new Mock<IRequestHandler<DeleteUserCommand, bool>>();
        delete.Setup(x => x.Handle(It.IsAny<DeleteUserCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IRequestHandler<DeleteUserCommand, bool>));
            services.AddSingleton(delete.Object);
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });
        Seed(factory);

        var client = SignedIn(factory, "admin@example.com", distinguishedName: editorDn);
        var response = await client.DeleteAsync($"/api/v1/users/{AliceId}");

        // Strict mock: nothing searched by login. Only the editor's own entry was read, and the
        // editor cannot delete.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new[] { editorDn }, readAt.Distinct());
    }

    [Fact]
    public async Task ATokenThatNamesNoEntry_IsRefusedAtAuthentication()
    {
        // What every token issued before the claim existed looks like. Authorising it would mean
        // searching for its login — the lookup the claim replaces.
        using var factory = FactoryForMemberOf("UserEditors");
        var client = SignedIn(factory, "editor@example.com", includeDistinguishedName: false);

        var response = await client.PutAsJsonAsync($"/api/v1/users/{AliceId}", new UserUpdateRequest { Name = "Alice" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASessionWhoseEntryIsGone_Fails401_SoTheConsoleSignsIn()
    {
        // Renamed, moved or deleted since sign-in. Nothing can be granted to an entry that is not
        // there, and signing in again is what finds where it went.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserByDnAsync(It.IsAny<string>())).ReturnsAsync((LdapUserInfo?)null);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });

        var client = SignedIn(factory, "moved@example.com");
        var response = await client.GetAsync($"/api/v1/users/{AliceId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
