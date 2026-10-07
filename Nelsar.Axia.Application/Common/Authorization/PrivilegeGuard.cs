using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Diagnostics;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Common.Authorization;

/// <summary>
/// Refuses to let a caller act on an account or a group that is more powerful than they are.
///
/// A permission like <c>users.update</c> says what kind of change a caller may make, not to whom,
/// and the directory work behind it runs as the service account, which can change anybody. So
/// <c>users.update</c> — held by the UserEditor role out of the box — was enough to reset an
/// administrator's password and sign in as them; <c>users.delete</c> to delete every
/// administrator; <c>groups.manage</c> to add oneself to the group mapped to Admin.
///
/// "More powerful" is measured two ways, because a target's power lives in two places:
///
/// In this application: the target's permissions must be a subset of the caller's. Nobody takes
/// over an account that can do something here that they cannot.
///
/// In the directory: an account or group the directory itself treats as privileged — Active
/// Directory's adminCount, set on Domain Admins and every protected group and their members, or a
/// group listed in DirectoryProtection — may only be changed by a caller holding every permission
/// here. The first rule alone missed these: a domain administrator whose groups map to no role is
/// a plain User to this application, and every editor outranked them.
///
/// And one account is out of bounds to everybody: the one the application binds as. Resetting its
/// password through the application breaks the application at best, and at worst hands its rights
/// in the directory to whoever chose the new password.
///
/// It is a second check, not a replacement for the first: the use case's own permission is still
/// enforced by <see cref="Behaviours.AuthorizationBehaviour{TRequest,TResponse}"/> before the
/// handler that calls this runs.
/// </summary>
internal sealed class PrivilegeGuard
{
    private readonly ActingUser _actingUser;
    private readonly IPermissionResolver _permissions;
    private readonly IRoleMappingResolver _roleMappings;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly DirectoryProtectionOptions _protection;
    private readonly ILogger<PrivilegeGuard> _logger;

    public PrivilegeGuard(
        ActingUser actingUser,
        IPermissionResolver permissions,
        IRoleMappingResolver roleMappings,
        IRolePermissionRepository rolePermissions,
        IOptions<DirectoryProtectionOptions> protection,
        ILogger<PrivilegeGuard> logger)
    {
        _actingUser = actingUser;
        _permissions = permissions;
        _roleMappings = roleMappings;
        _rolePermissions = rolePermissions;
        _protection = protection.Value;
        _logger = logger;
    }

    /// <summary>
    /// Passes when the account at <paramref name="targetDn"/> is not the service account, is not
    /// privileged in the directory unless the caller holds everything, and holds nothing here the
    /// caller lacks.
    /// </summary>
    /// <exception cref="PermissionDeniedException">Any of those does not hold.</exception>
    /// <exception cref="DomainNotFoundException">No entry has that DN.</exception>
    public async Task EnsureMayManageAccountAsync(string targetDn, CancellationToken cancellationToken = default)
    {
        if (_actingUser.IsSystem)
            return;

        // Resolved the way the target's own sign-in would resolve them, so "what can this account
        // do" has one answer whether it is asked by them or about them.
        var target = await _permissions.AssessEntryAsync(targetDn, _actingUser.ProviderId, cancellationToken)
                     ?? throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInLdap(targetDn));

        if (target.IsServiceAccount)
            throw Refuse(targetDn, "it is the service account", ApplicationMessages.ServiceAccountNotManaged());

        var held = await CallerPermissionsAsync(cancellationToken);

        if (target.IsProtected && !HoldsEverything(held))
            throw Refuse(targetDn, "it is privileged in the directory", ApplicationMessages.TargetIsProtected());

        EnsureCallerHolds(held, target.Permissions, targetDn);
    }

    /// <summary>
    /// Passes when the group is not privileged in the directory unless the caller holds
    /// everything, and when neither it nor a group called <paramref name="resultingName"/> is
    /// mapped to a role granting something the caller lacks. Either may be null: a group being
    /// created has no entry yet, and an update that keeps the name changes nothing about which
    /// mappings name it.
    /// </summary>
    /// <exception cref="PermissionDeniedException">The group confers more than the caller holds.</exception>
    public async Task EnsureMayManageGroupAsync(
        LdapGroup? existing,
        string? resultingName,
        CancellationToken cancellationToken = default)
    {
        if (_actingUser.IsSystem)
            return;

        var held = await CallerPermissionsAsync(cancellationToken);

        // Domain Admins and its kind: whoever can change their membership can make anyone —
        // themselves first — a directory administrator, whatever this application maps them to.
        if (existing is not null && (existing.IsPrivileged || _protection.Protects(existing.Dn)) && !HoldsEverything(held))
            throw Refuse(existing.Dn, "it is privileged in the directory", ApplicationMessages.TargetIsProtected());

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What membership of the group confers today — through a mapping on its exact DN, or a
        // legacy one on its CN.
        if (existing is not null)
            roles.UnionWith(await _roleMappings.ResolveGrantedRolesAsync([existing.Dn], _actingUser.ProviderId));

        // What it could confer under the name it is getting. Matched by name alone, which is wider
        // than a DN match on purpose: before the entry exists there is no DN to compare, and a
        // group that merely shares a mapped group's name is trouble even where it does not match.
        if (!string.IsNullOrWhiteSpace(resultingName))
            roles.UnionWith(await _roleMappings.ResolveRolesForGroupNameAsync(resultingName, _actingUser.ProviderId));

        if (roles.Count == 0)
            return;

        var conferred = await _rolePermissions.GetPermissionsForRolesAsync(roles, cancellationToken);
        EnsureCallerHolds(held, conferred, existing?.Dn ?? resultingName!);
    }

    private async Task<IReadOnlySet<string>> CallerPermissionsAsync(CancellationToken cancellationToken)
    {
        var callerDn = _actingUser.DistinguishedName;
        if (string.IsNullOrWhiteSpace(callerDn))
            throw new NotAuthenticatedException(ApplicationMessages.AuthenticationRequired());

        return await _permissions.ResolveAsync(callerDn, _actingUser.ProviderId, cancellationToken);
    }

    /// <summary>An administrator of this application: every permission it defines.</summary>
    private static bool HoldsEverything(IReadOnlySet<string> held) => Permissions.All.All(held.Contains);

    private void EnsureCallerHolds(IReadOnlySet<string> held, IReadOnlySet<string> required, string target)
    {
        var missing = required.Where(permission => !held.Contains(permission)).ToList();
        if (missing.Count == 0)
            return;

        throw Refuse(target, $"it confers {string.Join(", ", missing)}", ApplicationMessages.TargetHoldsMorePermissions());
    }

    /// <summary>
    /// As in AuthorizationBehaviour, the detail goes to the log and not the response: the operator
    /// investigating a refusal needs it, and the caller who was refused does not need a list of
    /// what the target can do.
    /// </summary>
    private PermissionDeniedException Refuse(string target, string reason, string message)
    {
        _logger.LogWarning("Refused to let {Caller} act on {Target}: {Reason}.",
            LogMask.Login(_actingUser.Subject ?? string.Empty),
            LogMask.DistinguishedName(target),
            reason);

        return new PermissionDeniedException(message);
    }
}
