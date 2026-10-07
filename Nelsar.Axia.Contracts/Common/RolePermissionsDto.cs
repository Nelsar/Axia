using System.Collections.Generic;

namespace Nelsar.Axia.Contracts.Common;

/// <param name="RoleName">One of the application's roles.</param>
/// <param name="Permissions">What the role is granted, in catalogue order.</param>
/// <param name="IsEditable">
/// False for Admin, which holds every permission and is not edited: an administrator who took
/// roles.manage away from it would leave nobody able to give it back.
/// </param>
public record RolePermissionsDto(string RoleName, IReadOnlyList<string> Permissions, bool IsEditable);
