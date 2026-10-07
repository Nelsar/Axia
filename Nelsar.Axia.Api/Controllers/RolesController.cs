using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Api.Controllers;

/// <summary>
/// What each application role is allowed to do. Which roles a person has is decided by their
/// directory groups, through <see cref="RoleMappingsController"/>; this is the other half.
///
/// Changes reach signed-in sessions as their cached permissions expire, within thirty seconds.
/// </summary>
[ApiController]
[Route("api/v1/roles")]
[Authorize(Policy = Permissions.RolesManage)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class RolesController : ControllerBase
{
    private readonly IRolesModule _roles;

    public RolesController(IRolesModule roles)
    {
        _roles = roles;
    }

    /// <summary>Every permission the application defines, and every role with what it holds.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(RolesOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var overview = await _roles.GetAllAsync();
        return Ok(overview);
    }

    /// <summary>
    /// Replaces what a role holds. Admin is not edited, roles.manage is not granted to any other
    /// role, and nobody grants a permission they do not hold themselves.
    /// </summary>
    [HttpPut("{roleName}/permissions")]
    [ProducesResponseType(typeof(RolePermissionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePermissions(string roleName, [FromBody] RolePermissionsUpdateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _roles.UpdatePermissionsAsync(roleName, request);
        return Ok(updated);
    }
}
