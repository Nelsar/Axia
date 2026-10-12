using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
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
    private readonly DirectoryProtectionOptions _protection;

    public PermissionResolver(
        ILdapService ldapService,
        IRoleMappingResolver roleMappingResolver,
        IRolePermissionRepository rolePermissions,
        IMemoryCache cache,
        IOptions<DirectoryProtectionOptions> protection)
    {
        _ldapService = ldapService;
        _roleMappingResolver = roleMappingResolver;
        _rolePermissions = rolePermissions;
        _cache = cache;
        _protection = protection.Value;
    }

    public async Task<IReadOnlySet<string>> ResolveAsync(
        string distinguishedName,
        Guid? providerId,
        CancellationToken cancellationToken = default)
    {
        var assessment = await AssessEntryAsync(distinguishedName, providerId, cancellationToken)
            // The session names an entry that is not there any more. Nothing it could be granted
            // is safe to guess at, and a fresh sign-in is what would find the account under its
            // new DN.
            ?? throw new NotAuthenticatedException(ApplicationMessages.SessionEntryGone());

        // Switched off in the directory after the token was issued. The token stays valid until
        // it expires, so this is where disabling an account ends its session: within CacheTtl,
        // on whichever request comes next. Only here, for the caller — as the target of someone
        // else's request the same entry is assessed as usual.
        if (assessment.IsDisabled)
            throw new NotAuthenticatedException(ApplicationMessages.SessionAccountDisabled());

        return assessment.Permissions;
    }

    public async Task<EntryAssessment?> AssessEntryAsync(
        string distinguishedName,
        Guid? providerId,
        CancellationToken cancellationToken = default)
    {
        // Keyed by provider too: the same group name can mean different things in different
        // domains, so a cached answer from one must never be served for another.
        var cacheKey = $"permissions:{providerId}:{distinguishedName.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out EntryAssessment? cached) && cached is not null)
            return cached;

        // Read at the DN rather than searched for by login. A search returns whichever entry the
        // directory lists first among those sharing the login, and "first" is not an identity.
        // Throws if the directory cannot be asked — not caught here on purpose, see the remarks
        // on IPermissionResolver. Absence is not cached: an entry restored a moment later should
        // not stay missing for the length of the TTL.
        var entry = await _ldapService.GetUserByDnAsync(distinguishedName);
        if (entry is null)
            return null;

        var roles = await _roleMappingResolver.ResolveRolesAsync(entry.Groups, providerId);
        var permissions = await _rolePermissions.GetPermissionsForRolesAsync(roles, cancellationToken);

        // Privileged by the directory's own reckoning, whatever roles the entry maps to here:
        // adminCount in Active Directory, or a group named in DirectoryProtection.
        var assessment = new EntryAssessment(
            permissions,
            IsProtected: entry.IsPrivileged || entry.Groups.Any(_protection.Protects),
            IsServiceAccount: entry.IsServiceAccount,
            IsDisabled: entry.IsDisabled);

        _cache.Set(cacheKey, assessment, CacheTtl);
        return assessment;
    }
}
