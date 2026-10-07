using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

/// <summary>
/// The complete set of permissions a role is to hold. Replaces what the role had: a permission
/// left out is revoked.
/// </summary>
public class RolePermissionsUpdateRequest
{
    [Required]
    public List<string> Permissions { get; set; } = [];
}
