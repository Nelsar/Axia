using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Api.Controllers;

[ApiController]
[Route("api/v1/rate-limit-settings")]
[Authorize(Policy = Permissions.RateLimitsManage)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class RateLimitSettingsController : ControllerBase
{
    private readonly IRateLimitSettingsModule _rateLimitSettings;

    public RateLimitSettingsController(IRateLimitSettingsModule rateLimitSettings)
    {
        _rateLimitSettings = rateLimitSettings;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RateLimitSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var items = await _rateLimitSettings.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{policyName}")]
    [ProducesResponseType(typeof(RateLimitSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByPolicyName(string policyName)
    {
        var item = await _rateLimitSettings.GetByPolicyNameAsync(policyName);
        if (item == null) return NotFound();

        return Ok(item);
    }

    /// <summary>
    /// Creates or updates the settings for a named policy, e.g. "auth-login".
    /// Takes effect within the provider's cache TTL (currently 30s) — no restart needed.
    /// </summary>
    [HttpPut("{policyName}")]
    [ProducesResponseType(typeof(RateLimitSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(string policyName, [FromBody] RateLimitSettingsUpdateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var saved = await _rateLimitSettings.UpsertAsync(policyName, request);

        return Ok(saved);
    }
}
