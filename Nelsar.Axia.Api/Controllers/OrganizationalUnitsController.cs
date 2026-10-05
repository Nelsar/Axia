using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Api.Controllers;

/// <summary>
/// Organizational unit management, backed by the directory itself through
/// <see cref="IOrganizationalUnitsModule"/>.
///
/// A unit is addressed here by <c>id</c>, which is a hash of its distinguished name — so it is
/// stable without anything being stored, and it changes when the unit is renamed or moved,
/// because the DN is the unit's identity as far as the directory is concerned. A client holding
/// an id across a rename will get a 404 and should re-read the listing.
///
/// This file did not change when the feature was built: the actions always called the service,
/// and the previous HTTP 501 came from the exception it raised.
/// </summary>
[ApiController]
[Route("api/v1/organizational-units")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class OrganizationalUnitsController : ControllerBase
{
    private readonly IOrganizationalUnitsModule _organizationalUnits;

    public OrganizationalUnitsController(IOrganizationalUnitsModule organizationalUnits)
    {
        _organizationalUnits = organizationalUnits;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.OrganizationalUnitsRead)]
    [ProducesResponseType(typeof(PagedResponse<OrganizationalUnitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? filter = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? pageToken = null)
    {
        var result = await _organizationalUnits.SearchAsync(filter, pageSize, pageToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = Permissions.OrganizationalUnitsRead)]
    [ProducesResponseType(typeof(OrganizationalUnitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _organizationalUnits.GetByIdAsync(id);
        if (item == null)
            return NotFound();

        return Ok(item);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.OrganizationalUnitsManage)]
    [ProducesResponseType(typeof(OrganizationalUnitDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationalUnitRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _organizationalUnits.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Permissions.OrganizationalUnitsManage)]
    [ProducesResponseType(typeof(OrganizationalUnitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrganizationalUnitRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _organizationalUnits.UpdateAsync(id, request);
        if (updated == null)
            return NotFound();

        return Ok(updated);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = Permissions.OrganizationalUnitsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _organizationalUnits.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
