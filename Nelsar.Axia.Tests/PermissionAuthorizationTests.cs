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

    private static ApiTestFactory FactoryForMemberOf(params string[] groupNames)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserGroupsAsync(It.IsAny<string>()))
            .ReturnsAsync(groupNames.Select(g => $"CN={g},OU=Groups,DC=example,DC=com").ToList());

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

        db.RoleMappings.AddRange(
            RoleMapping.Create(null, "UserEditors", UserRoles.UserEditor, 0),
            RoleMapping.Create(null, "UserDeleters", UserRoles.UserDeleter, 0),
            RoleMapping.Create(null, "UserCreators", UserRoles.UserCreator, 0));

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

    private static HttpClient SignedIn(ApiTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            // The role in the token is intentionally the weakest one available. Authorisation
            // must reach its verdict from directory membership regardless of what is claimed here.
            TestAuth.GenerateToken(UserRoles.User, email));
        return client;
    }

    [Fact]
    public async Task MembershipGrantsExactlyTheMappedPermission()
    {
        using var factory = FactoryForMemberOf("UserEditors");
        var client = SignedIn(factory, "editor@example.com");

        var update = await client.PutAsJsonAsync($"/api/users/{AliceId}", new UserUpdateRequest { Name = "Alice" });
        var delete = await client.DeleteAsync($"/api/users/{AliceId}");

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task TwoGroupsGrantTheUnionOfTheirPermissions()
    {
        using var factory = FactoryForMemberOf("UserEditors", "UserDeleters");
        var client = SignedIn(factory, "editor-and-deleter@example.com");

        var update = await client.PutAsJsonAsync($"/api/users/{AliceId}", new UserUpdateRequest { Name = "Alice" });
        var delete = await client.DeleteAsync($"/api/users/{AliceId}");

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

        var read = await client.GetAsync($"/api/users/{AliceId}");
        var delete = await client.DeleteAsync($"/api/users/{AliceId}");

        Assert.NotEqual(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task AdministrativeEndpoint_IsRefusedWithoutItsPermission()
    {
        using var factory = FactoryForMemberOf("UserEditors");
        var client = SignedIn(factory, "editor-only@example.com");

        var response = await client.PostAsJsonAsync("/api/users", new UserCreateRequest
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
        ldap.Setup(x => x.GetUserGroupsAsync(It.IsAny<string>()))
            .ThrowsAsync(new DirectoryUnavailableException("The directory could not be reached."));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });

        var client = SignedIn(factory, "anyone@example.com");
        var response = await client.GetAsync($"/api/users/{AliceId}");

        // 403 would tell the user they lack a right they may well hold, and would hide the
        // outage. Membership unknown means the question cannot be answered, not answered "no".
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task AnAccountWithNoMailAttribute_IsLookedUpByItsLogin_NotByItsDerivedId()
    {
        var lookedUpBy = new List<string>();

        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserGroupsAsync(It.IsAny<string>()))
            .Callback<string>(lookedUpBy.Add)
            .ReturnsAsync(new List<string> { "CN=UserEditors,OU=Groups,DC=example,DC=com" });

        var update = new Mock<IRequestHandler<UpdateUserCommand, UserDto?>>();
        update.Setup(x => x.Handle(It.IsAny<UpdateUserCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { Id = AliceId, Username = "Anuar Mukatov", Name = "Anuar Mukatov" });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IRequestHandler<UpdateUserCommand, UserDto?>));
            services.AddSingleton(update.Object);
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });

        Seed(factory);

        // The token such an account now gets: nothing to put in the email claim, login in
        // preferred_username.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestAuth.GenerateToken(UserRoles.User, email: "", preferredUsername: "Anuar Mukatov"));

        var response = await client.PutAsJsonAsync($"/api/users/{AliceId}", new UserUpdateRequest { Name = "Anuar Mukatov" });

        // The regression this pins: with the email claim empty, subject resolution fell through
        // to NameIdentifier — which for a JWT is sub, a derived Guid. Every request from such an
        // account then asked the directory for an entry named by a Guid and failed with 502.
        Assert.Equal(new[] { "Anuar Mukatov" }, lookedUpBy.Distinct());
        Assert.DoesNotContain(lookedUpBy, value => Guid.TryParse(value, out _));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
