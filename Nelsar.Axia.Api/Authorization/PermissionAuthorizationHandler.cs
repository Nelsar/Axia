using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Api.Authorization;

/// <summary>
/// Answers "may this principal do X?" by resolving their permissions now, rather than trusting
/// a claim minted at sign-in.
///
/// Nothing is caught here. If the directory cannot be reached the resolver throws and the
/// request fails with 502 — refusing to answer is the only safe outcome, because the
/// alternative is granting whatever an empty permission set happens to allow.
/// </summary>
internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionResolver _permissionResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionAuthorizationHandler(
        IPermissionResolver permissionResolver,
        IHttpContextAccessor httpContextAccessor)
    {
        _permissionResolver = permissionResolver;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var subject = RequestPrincipal.Subject(context.User);
        if (subject is null)
        {
            // Not signed in, or signed in without anything we can look up. Leaving the
            // requirement unmet produces 401/403 rather than an exception.
            return;
        }

        var permissions = await _permissionResolver.ResolveAsync(
            subject,
            RequestPrincipal.ProviderId(_httpContextAccessor.HttpContext));

        if (permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
