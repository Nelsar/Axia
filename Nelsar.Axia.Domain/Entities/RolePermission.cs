using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Common;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Grants one permission to one role. This is the layer that was missing: <see cref="RoleMapping"/>
/// already let an administrator say "this LDAP group is UserCreator", but what a UserCreator may
/// actually do was compiled into <c>[Authorize(Roles = ...)]</c> and could not be changed without
/// a redeploy.
///
/// Keyed by role <em>name</em>, exactly as <see cref="RoleMapping"/> is, so the two agree without
/// a join table in between. The cost is no referential integrity between them: renaming a role
/// means updating both. A Roles table would fix that and is worth adding once roles need
/// metadata of their own.
/// </summary>
public class RolePermission
{
    public const int MaximumRoleNameLength = 128;

    /// <summary>For EF materialisation only.</summary>
    private RolePermission()
    {
    }

    public Guid Id { get; private set; }

    public string RoleName { get; private set; } = string.Empty;

    /// <summary>One of <see cref="Permissions"/>.</summary>
    public string Permission { get; private set; } = string.Empty;

    /// <summary>
    /// Grants a permission from the catalogue to a role.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The role name is blank, or the permission is not one the application defines. The second
    /// check is the point of the factory: <see cref="Permissions"/> explains that a permission an
    /// administrator could invent but nothing enforces is a checkbox that grants nothing and that
    /// they would nonetheless believe in. Refusing to store one keeps that promise.
    /// </exception>
    public static RolePermission Grant(string? roleName, string? permission)
    {
        var granted = DomainGuard.RequiredText(permission, nameof(Permission), MaximumRoleNameLength);

        if (!Permissions.IsKnown(granted))
            throw new DomainValidationException(DomainMessages.PermissionUnknown(granted));

        return new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleName = DomainGuard.RequiredText(roleName, nameof(RoleName), MaximumRoleNameLength),
            Permission = granted
        };
    }
}
