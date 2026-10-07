using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Diagnostics;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Common.Authorization;

/// <summary>
/// Refuses to let a caller act on an account or a group that confers permissions the caller does
/// not hold.
///
/// A permission like <c>users.update</c> says what kind of change a caller may make, not to whom,
/// and the directory work behind it runs as the service account, which can change anybody. So
/// <c>users.update</c> — held by the UserEditor role out of the box — was enough to reset an
/// administrator's password and sign in as them; <c>users.delete</c> to delete every
/// administrator; <c>groups.manage</c> to add oneself to the group mapped to Admin. Each is the
/// whole permission catalogue reached from one entry in it.
///
/// The rule is the plainest one that closes that: the target's permissions must be a subset of
/// the caller's. Nobody can take over an account that can do something they cannot, or put
/// anyone — themselves included — into a group that grants it. An administrator, holding
/// everything, is never refused; a caller acting on their own account always passes.
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
    private readonly ILogger<PrivilegeGuard> _logger;

    public PrivilegeGuard(
        ActingUser actingUser,
        IPermissionResolver permissions,
        IRoleMappingResolver roleMappings,
        IRolePermissionRepository rolePermissions,
        ILogger<PrivilegeGuard> logger)
    {
        _actingUser = actingUser;
        _permissions = permissions;
        _roleMappings = roleMappings;
        _rolePermissions = rolePermissions;
        _logger = logger;
    }

    /// <summary>
    /// Passes when the account at <paramref name="targetDn"/> holds nothing the caller lacks.
    /// </summary>
    /// <exception cref="PermissionDeniedException">The account holds more than the caller.</exception>
    /// <exception cref="DomainNotFoundException">No entry has that DN.</exception>
    public async Task EnsureMayManageAccountAsync(string targetDn, CancellationToken cancellationToken = default)
    {
        if (_actingUser.IsSystem)
            return;

        // Resolved the way the target's own sign-in would resolve them, so "what can this account
        // do" has one answer whether it is asked by them or about them.
        var target = await _permissions.ResolveForEntryAsync(targetDn, _actingUser.ProviderId, cancellationToken)
                     ?? throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInLdap(targetDn));

        await EnsureCallerHoldsAsync(target, targetDn, cancellationToken);
    }

    /// <summary>
    /// Passes when neither the group at <paramref name="existingDn"/> nor a group called
    /// <paramref name="resultingName"/> is mapped to a role granting something the caller lacks.
    /// Either may be null: a group being created has no DN yet, and an update that keeps the name
    /// changes nothing about which mappings name it.
    /// </summary>
    /// <exception cref="PermissionDeniedException">The group confers more than the caller holds.</exception>
    public async Task EnsureMayManageGroupAsync(
        string? existingDn,
        string? resultingName,
        CancellationToken cancellationToken = default)
    {
        if (_actingUser.IsSystem)
            return;

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What membership of the group confers today — through a mapping on its exact DN, or a
        // legacy one on its CN.
        if (!string.IsNullOrWhiteSpace(existingDn))
            roles.UnionWith(await _roleMappings.ResolveGrantedRolesAsync([existingDn], _actingUser.ProviderId));

        // What it could confer under the name it is getting. Matched by name alone, which is wider
        // than a DN match on purpose: before the entry exists there is no DN to compare, and a
        // group that merely shares a mapped group's name is trouble even where it does not match.
        if (!string.IsNullOrWhiteSpace(resultingName))
            roles.UnionWith(await _roleMappings.ResolveRolesForGroupNameAsync(resultingName, _actingUser.ProviderId));

        if (roles.Count == 0)
            return;

        var conferred = await _rolePermissions.GetPermissionsForRolesAsync(roles, cancellationToken);
        await EnsureCallerHoldsAsync(conferred, existingDn ?? resultingName!, cancellationToken);
    }

    private async Task EnsureCallerHoldsAsync(
        IReadOnlySet<string> required,
        string target,
        CancellationToken cancellationToken)
    {
        var callerDn = _actingUser.DistinguishedName;
        if (string.IsNullOrWhiteSpace(callerDn))
            throw new NotAuthenticatedException(ApplicationMessages.AuthenticationRequired());

        var held = await _permissions.ResolveAsync(callerDn, _actingUser.ProviderId, cancellationToken);
        var missing = required.Where(permission => !held.Contains(permission)).ToList();

        if (missing.Count == 0)
            return;

        // As in AuthorizationBehaviour, the detail goes to the log and not the response: the
        // operator investigating a refusal needs it, and the caller who was refused does not need
        // a list of what the target can do.
        _logger.LogWarning(
            "Refused to let {Caller} act on {Target}, which confers {Missing} the caller does not hold.",
            LogMask.Login(_actingUser.Subject ?? string.Empty),
            LogMask.DistinguishedName(target),
            string.Join(", ", missing));

        throw new PermissionDeniedException(ApplicationMessages.TargetHoldsMorePermissions());
    }
}
