using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Features.Providers.Commands;
using Nelsar.Axia.Application.Features.Providers.Queries;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Authorisation as the Application layer enforces it, independently of any controller attribute.
///
/// The point of the second check is that it holds for a request that never went through MVC —
/// a background job, another front end, a handler dispatching a command — so these drive the
/// pipeline directly, exactly as such a caller would.
/// </summary>
public class UseCaseAuthorizationTests
{
    private static ISender CreateSender(
        IProviderRepository repo,
        ICurrentUser currentUser,
        IPermissionResolver permissions)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(repo);
        services.AddSingleton(new Mock<IEncryptionService>().Object);
        services.AddSingleton(currentUser);
        services.AddSingleton(permissions);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task AGuardedUseCase_RefusesWhenNobodyIsSignedIn()
    {
        var repo = new Mock<IProviderRepository>();

        var sender = CreateSender(
            repo.Object,
            new TestCurrentUser(subject: null),
            new FixedPermissionResolver([.. Permissions.All]));

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => sender.Send(new GetAllProvidersQuery()));

        repo.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task AGuardedUseCase_RefusesACallerWithoutThePermission()
    {
        var repo = new Mock<IProviderRepository>();

        // Holds something, just not providers.manage.
        var sender = CreateSender(
            repo.Object,
            new TestCurrentUser("someone@example.com"),
            new FixedPermissionResolver(Permissions.UsersRead));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new GetAllProvidersQuery()));

        repo.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task AGuardedUseCase_RunsForACallerWhoHoldsThePermission()
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var sender = CreateSender(
            repo.Object,
            new TestCurrentUser("admin@example.com"),
            new FixedPermissionResolver(Permissions.ProvidersManage));

        await sender.Send(new GetAllProvidersQuery());

        repo.Verify(x => x.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task TheRefusalMessage_DoesNotNameTheMissingPermission()
    {
        var sender = CreateSender(
            new Mock<IProviderRepository>().Object,
            new TestCurrentUser("someone@example.com"),
            new FixedPermissionResolver(Permissions.UsersRead));

        var failure = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new GetAllProvidersQuery()));

        // Which permission opens a door is a map of the authorisation model. It belongs in the
        // log, not in a response to whoever is probing for it.
        Assert.DoesNotContain("providers.manage", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizationRunsBeforeValidation_SoARefusedCallerLearnsNothingAboutThePayload()
    {
        var sender = CreateSender(
            new Mock<IProviderRepository>().Object,
            new TestCurrentUser("someone@example.com"),
            new FixedPermissionResolver(Permissions.UsersRead));

        // Blank name and host would both fail validation. The caller is refused first, so the
        // 400 that would have described the command's fields is never produced.
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new CreateProviderCommand(
                "", "", 389, "", true, null, "", "", null, "(uid={0})", 500)));
    }

    [Fact]
    public async Task SystemWork_RunsWithoutAPrincipal()
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        // A queued job has no signed-in caller to resolve permissions for; it was authorised when
        // a person asked for it.
        var sender = CreateSender(
            repo.Object,
            new TestCurrentUser(subject: null, isSystem: true),
            new FixedPermissionResolver());

        await sender.Send(new GetAllProvidersQuery());

        repo.Verify(x => x.GetAllAsync(), Times.Once);
    }

    [Fact]
    public void EveryGuardedUseCase_DeclaresAPermissionFromTheCatalogue()
    {
        // A requirement naming a permission nothing registers a policy for would refuse everyone,
        // and would do so only on the endpoint that happens to dispatch it.
        var guarded = typeof(GetAllProvidersQuery).Assembly
            .GetTypes()
            .Where(type => typeof(IRequirePermission).IsAssignableFrom(type) && !type.IsInterface)
            .ToList();

        Assert.NotEmpty(guarded);

        foreach (var type in guarded)
        {
            var instance = (IRequirePermission)System.Runtime.CompilerServices
                .RuntimeHelpers.GetUninitializedObject(type);

            Assert.True(Permissions.IsKnown(instance.RequiredPermission),
                $"{type.Name} requires '{instance.RequiredPermission}', which is not in the catalogue.");
        }
    }

    [Fact]
    public void CreateUserCommand_CarriesNoRequirement_BecauseTwoRoutesReachIt()
    {
        // POST /api/v1/users dispatches this command behind users.create; POST /api/auth/register
        // dispatches the very same one for a caller who has not authenticated at all. A
        // requirement attached here would close both, so the difference between them is the
        // host's to enforce — and it does: registration answers 404 unless a deployment sets
        // Auth:AllowSelfRegistration, which none does by default (see AuthOptions, and
        // UsersControllerIntegrationTests.AuthRegister_IsNotThere_UnlessADeploymentAsksForIt).
        //
        // This test states the arrangement so the next person meets it deliberately rather than
        // reading the missing requirement as an oversight.
        Assert.False(typeof(IRequirePermission).IsAssignableFrom(typeof(CreateUserCommand)));
        Assert.False(typeof(IRequirePermission).IsAssignableFrom(typeof(ProcessBulkUsersCommand)));
    }

    [Fact]
    public void TheDirectoryPicker_IsOpenOnPurpose_BecauseItIsReadBeforeAnyoneCanSignIn()
    {
        // GET /api/v1/providers/selectable answers "which directories are there?", and a caller
        // has to answer that before they can be authenticated against one. Guarding it would put
        // the answer behind providers.manage, which is where it used to be and why only an
        // administrator could sign in to anything but the default directory.
        //
        // What keeps this safe is not a permission but the projection: SelectableProviderDto
        // carries an id and a name and nothing about how the directory is reached. That is
        // asserted against the wire bytes in SelectableProvidersTests.
        Assert.False(typeof(IRequirePermission).IsAssignableFrom(typeof(GetSelectableProvidersQuery)));

        // Its administrative sibling stays guarded — the two are one edit apart.
        Assert.True(typeof(IRequirePermission).IsAssignableFrom(typeof(GetAllProvidersQuery)));
    }
}
