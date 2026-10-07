using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// Which permissions each role holds — the second half of authorisation, after
/// <see cref="IRoleMappingsModule"/> has decided which roles a person's groups give them.
/// </summary>
public interface IRolesModule : IOpencoreModule
{
    Task<RolesOverviewDto> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the role's permissions with exactly the ones given.</summary>
    Task<RolePermissionsDto> UpdatePermissionsAsync(
        string roleName,
        RolePermissionsUpdateRequest request,
        CancellationToken cancellationToken = default);
}
