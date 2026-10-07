using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.Roles.Queries;

/// <summary>Every role and what it is granted, with the catalogue the grants are drawn from.</summary>
internal sealed record GetRolesQuery : IRequest<RolesOverviewDto>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RolesManage;
}

internal sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, RolesOverviewDto>
{
    private readonly IRolePermissionRepository _grants;

    public GetRolesQueryHandler(IRolePermissionRepository grants) => _grants = grants;

    public async Task<RolesOverviewDto> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var grants = (await _grants.GetAllAsync(cancellationToken))
            .ToLookup(grant => grant.RoleName, grant => grant.Permission, StringComparer.OrdinalIgnoreCase);

        var roles = UserRoles.All
            .Select(role => RoleGrants.ToDto(role, grants[role]))
            .ToList();

        return new RolesOverviewDto(Permissions.All, roles);
    }
}

internal static class RoleGrants
{
    /// <summary>
    /// The one role that is not edited: it holds every permission, and taking roles.manage away
    /// from it would leave nobody able to give it back.
    /// </summary>
    public static bool IsEditable(string roleName) =>
        !string.Equals(roleName, UserRoles.Admin, StringComparison.OrdinalIgnoreCase);

    /// <summary>In catalogue order, so a role reads the same way every time it is listed.</summary>
    public static RolePermissionsDto ToDto(string roleName, IEnumerable<string> granted)
    {
        var held = granted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new RolePermissionsDto(
            roleName,
            Permissions.All.Where(held.Contains).ToList(),
            IsEditable(roleName));
    }
}
