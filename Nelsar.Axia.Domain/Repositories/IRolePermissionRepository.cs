using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Domain.Repositories;

public interface IRolePermissionRepository
{
    Task<IEnumerable<RolePermission>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every permission held by any of the given roles, de-duplicated. A user's rights are the
    /// union across their roles, so holding two roles can only ever add.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsForRolesAsync(
        IEnumerable<string> roleNames,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<RolePermission>> GetForRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>Replaces a role's grants wholesale, so the caller never has to diff.</summary>
    Task SetPermissionsForRoleAsync(
        string roleName,
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default);
}
