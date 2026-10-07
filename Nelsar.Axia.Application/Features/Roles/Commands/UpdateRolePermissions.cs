using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Roles.Queries;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Application.Features.Roles.Commands;

/// <summary>
/// Replaces what a role is granted.
///
/// The grants table was the half of authorisation nobody could edit: role mappings had an API and
/// a screen, and what each role then allowed was whatever the seeder wrote on first run, changed
/// only by hand in the database. This is that screen's backend, with three rules the table never
/// needed while nobody could write to it:
///
/// Admin is not edited. It is the role that holds roles.manage, and the one place a mistake here
/// cannot be undone from here: take roles.manage from it and nobody is left to give it back.
///
/// roles.manage is not granted to any other role. Whoever holds it can map their own group to
/// Admin, so it is Admin under another name; giving it to a role is giving that role everything,
/// and the way to do that is the role mapping, where it is visible as what it is.
///
/// Nobody grants what they do not hold. With the rule above that changes nothing for Admin, but
/// it is the same subset rule PrivilegeGuard applies to accounts, and it keeps this from being a
/// way around it should roles.manage ever be held more widely.
///
/// Changes take effect as cached permission sets expire, within PermissionResolver's TTL.
/// </summary>
internal sealed record UpdateRolePermissionsCommand(string RoleName, IReadOnlyList<string> Permissions)
    : IRequest<RolePermissionsDto>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Domain.Authorization.Permissions.RolesManage;

    /// <inheritdoc />
    public string AuditTarget => RoleName;

    /// <inheritdoc />
    public string? AuditDescription => $"permissions={string.Join(",", Permissions ?? [])}";
}

internal sealed class UpdateRolePermissionsCommandValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsCommandValidator()
    {
        RuleFor(c => c.RoleName).NotEmpty();
        RuleFor(c => c.Permissions).NotNull();
    }
}

internal sealed class UpdateRolePermissionsCommandHandler
    : IRequestHandler<UpdateRolePermissionsCommand, RolePermissionsDto>
{
    private readonly IRolePermissionRepository _grants;
    private readonly ActingUser _actingUser;
    private readonly IPermissionResolver _permissions;
    private readonly ILogger<UpdateRolePermissionsCommandHandler> _logger;

    public UpdateRolePermissionsCommandHandler(
        IRolePermissionRepository grants,
        ActingUser actingUser,
        IPermissionResolver permissions,
        ILogger<UpdateRolePermissionsCommandHandler> logger)
    {
        _grants = grants;
        _actingUser = actingUser;
        _permissions = permissions;
        _logger = logger;
    }

    public async Task<RolePermissionsDto> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = UserRoles.Canonical(request.RoleName)
                   ?? throw new DomainNotFoundException(ApplicationMessages.RoleUnknown(request.RoleName));

        if (!RoleGrants.IsEditable(role))
            throw new DomainValidationException(ApplicationMessages.RoleNotEditable(role));

        var requested = new List<string>();
        foreach (var permission in request.Permissions.Select(p => (p ?? string.Empty).Trim()).Where(p => p.Length > 0))
        {
            // Spelled as the catalogue spells it, which is what the authorisation checks compare.
            var known = Permissions.All.FirstOrDefault(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase))
                        ?? throw new DomainValidationException(DomainMessages.PermissionUnknown(permission));

            if (!requested.Contains(known))
                requested.Add(known);
        }

        if (requested.Contains(Permissions.RolesManage))
            throw new DomainValidationException(ApplicationMessages.PermissionNotGrantable(Permissions.RolesManage));

        var current = (await _grants.GetForRoleAsync(role, cancellationToken))
            .Select(grant => grant.Permission)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await EnsureCallerHoldsAsync(requested.Where(p => !current.Contains(p)).ToList(), role, cancellationToken);

        await _grants.SetPermissionsForRoleAsync(role, requested, cancellationToken);

        _logger.LogInformation("Role {Role} now holds {Permissions}.", role, string.Join(", ", requested));
        return RoleGrants.ToDto(role, requested);
    }

    private async Task EnsureCallerHoldsAsync(IReadOnlyList<string> added, string role, CancellationToken cancellationToken)
    {
        if (added.Count == 0 || _actingUser.IsSystem)
            return;

        var callerDn = _actingUser.DistinguishedName;
        if (string.IsNullOrWhiteSpace(callerDn))
            throw new NotAuthenticatedException(ApplicationMessages.AuthenticationRequired());

        var held = await _permissions.ResolveAsync(callerDn, _actingUser.ProviderId, cancellationToken);
        var missing = added.Where(permission => !held.Contains(permission)).ToList();
        if (missing.Count == 0)
            return;

        _logger.LogWarning("Refused to grant {Missing} to {Role}: the caller does not hold it.",
            string.Join(", ", missing), role);
        throw new PermissionDeniedException(ApplicationMessages.CannotGrantUnheldPermission());
    }
}
