using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Api.Controllers;

[ApiController]
[Route("api/v1/audit-events")]
[Authorize(Policy = Permissions.AuditRead)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AuditController : ControllerBase
{
    private readonly IAuditModule _audit;

    public AuditController(IAuditModule audit)
    {
        _audit = audit;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AuditEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromQuery] int pageSize = 50, [FromQuery] string? pageToken = null)
    {
        var result = await _audit.GetRecentAsync(pageSize, pageToken);
        return Ok(result);
    }
}
