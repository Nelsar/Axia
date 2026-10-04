using Microsoft.Extensions.Caching.Memory;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Application.Services;

internal class PermissionResolver : IPermissionResolver
{
    /// <summary>
    /// How long a resolved permission set is reused. This number <em>is</em> the revocation
    /// delay: after an administrator takes a permission away, holders keep it for at most this
    /// long. It is also what stops every request turning into an LDAP round trip, so it trades
    /// directly against directory load. Matches the 30s used by the rate limit settings
    /// provider, for one number to reason about rather than two.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly ILdapService _ldapService;
    private readonly IRoleMappingResolver _roleMappingResolver;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IMemoryCache _cache;

    public PermissionResolver(
        ILdapService ldapService,
        IRoleMappingResolver roleMappingResolver,
        IRolePermissionRepository rolePermissions,
        IMemoryCache cache)
    {
        _ldapService = ldapService;
        _roleMappingResolver = roleMappingResolver;
        _rolePermissions = rolePermissions;
        _cache = cache;
    }

    public async Task<IReadOnlySet<string>> ResolveAsync(
        string subject,
        Guid? providerId,
        CancellationToken cancellationToken = default)
    {
        // Keyed by provider too: the same group name can mean different things in different
        // domains, so a cached answer from one must never be served for another.
        var cacheKey = $"permissions:{providerId}:{subject.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlySet<string>? cached) && cached is not null)
            return cached;

        // Throws if membership cannot be determined. Not caught here on purpose — see the
        // remarks on IPermissionResolver.
        var groups = await _ldapService.GetUserGroupsAsync(subject);

        // The port promises a list; null would mean a provider silently answered "unknown", and
        // treating that as "in no groups" is the exact failure this design refuses to make.
        if (groups is null)
        {
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryGroupMembershipFailed(subject));
        }

        var roles = await _roleMappingResolver.ResolveRolesAsync(groups, providerId);
        var permissions = await _rolePermissions.GetPermissionsForRolesAsync(roles, cancellationToken);

        _cache.Set(cacheKey, permissions, CacheTtl);
        return permissions;
    }
}
