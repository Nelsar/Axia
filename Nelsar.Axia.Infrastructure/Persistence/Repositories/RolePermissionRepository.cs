using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.Persistence.Repositories;

internal class RolePermissionRepository : IRolePermissionRepository
{
    private readonly IAppDbContext _db;

    public RolePermissionRepository(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<RolePermission>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.RolePermissions.AsNoTracking()
            .OrderBy(x => x.RoleName).ThenBy(x => x.Permission)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> GetPermissionsForRolesAsync(
        IEnumerable<string> roleNames,
        CancellationToken cancellationToken = default)
    {
        var roles = roleNames.ToList();
        if (roles.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Role names are compared case-insensitively everywhere else (RoleMappingResolver matches
        // group CNs that way), so the lookup is done in memory rather than letting the provider
        // pick a collation that disagrees.
        var all = await _db.RolePermissions.AsNoTracking().ToListAsync(cancellationToken);

        return all
            .Where(x => roles.Contains(x.RoleName, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Permission)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<RolePermission>> GetForRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var all = await _db.RolePermissions.AsNoTracking().ToListAsync(cancellationToken);
        return all
            .Where(x => string.Equals(x.RoleName, roleName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Permission)
            .ToList();
    }

    public async Task SetPermissionsForRoleAsync(
        string roleName,
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.RolePermissions
            .Where(x => x.RoleName == roleName)
            .ToListAsync(cancellationToken);

        _db.RolePermissions.RemoveRange(existing);

        // Grant rejects a permission that is not in the catalogue, so a typo in an administrator's
        // request fails here rather than becoming a row that grants nothing and looks like it does.
        foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _db.RolePermissions.Add(RolePermission.Grant(roleName, permission));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
