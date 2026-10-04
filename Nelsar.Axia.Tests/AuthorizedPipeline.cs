using Microsoft.Extensions.DependencyInjection;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

internal static class AuthorizedPipeline
{
    /// <summary>
    /// Puts a caller holding every permission behind the MediatR pipeline.
    ///
    /// Use cases declare what they require (see <c>IRequirePermission</c>) and
    /// AuthorizationBehaviour enforces it, so a pipeline built with no principal refuses before
    /// any handler runs. A test about what a use case <em>does</em> should not have to be a test
    /// of who may call it as well; refusal is covered on its own by
    /// <see cref="UseCaseAuthorizationTests"/>.
    ///
    /// This mirrors what ApiTestFactory does for tests that go over HTTP, and for the same reason.
    /// </summary>
    public static IServiceCollection WithAuthorizedCaller(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("admin@example.com"));
        services.AddSingleton<IPermissionResolver>(new FixedPermissionResolver([.. Permissions.All]));
        return services;
    }
}
