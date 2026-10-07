using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;

namespace Nelsar.Axia.Api.Controllers;

/// <summary>
/// The signed-in caller, asking about themselves.
///
/// Not under /api/v1/auth: that controller is rate-limited as a sign-in surface, five requests a
/// minute, which a console asking this on every page would exhaust on its own.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class MeController : ControllerBase
{
    private readonly IAuthModule _auth;

    public MeController(IAuthModule auth)
    {
        _auth = auth;
    }

    /// <summary>
    /// What the caller may do now, in catalogue order — the union of everything their groups'
    /// roles grant, which is what every other endpoint checks against.
    /// </summary>
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions()
    {
        var permissions = await _auth.GetMyPermissionsAsync();
        return Ok(permissions);
    }
}
