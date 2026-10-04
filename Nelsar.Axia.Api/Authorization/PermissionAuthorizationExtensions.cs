using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Api.Authorization;

public static class PermissionAuthorizationExtensions
{
    /// <summary>
    /// Registers one policy per entry in <see cref="Permissions.All"/>, so an endpoint can ask
    /// for <c>[Authorize(Policy = Permissions.UsersDelete)]</c> and get exactly that check.
    ///
    /// Generated from the catalogue rather than listed by hand: a new permission then cannot be
    /// referenced by an endpoint without a policy existing for it, which would otherwise fail at
    /// runtime on the first request instead of being impossible.
    /// </summary>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        // Scoped, not singleton: the handler depends on IPermissionResolver, which depends on a
        // DbContext. A singleton would capture the first request's scope and keep its DbContext
        // for the lifetime of the process.
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // The same principal, offered to the Application layer so its use cases can run their own
        // permission check instead of trusting that a route attribute was remembered. Replaces the
        // fail-closed default AddApplicationLayer registers.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission)));
            }
        });

        return services;
    }
}
