using System.Collections.Generic;

namespace Nelsar.Axia.Contracts.Common;

/// <param name="Permissions">Every permission the application defines, in catalogue order.</param>
/// <param name="Roles">Every role, with what it is granted.</param>
public record RolesOverviewDto(IReadOnlyList<string> Permissions, IReadOnlyList<RolePermissionsDto> Roles);
