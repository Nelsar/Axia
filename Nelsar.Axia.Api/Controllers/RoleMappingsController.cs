using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Api.Controllers;

[ApiController]
[Route("api/v1/role-mappings")]
[Authorize(Policy = Permissions.RolesManage)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class RoleMappingsController : ControllerBase
{
    private readonly IRoleMappingsModule _roleMappings;

    public RoleMappingsController(IRoleMappingsModule roleMappings)
    {
        _roleMappings = roleMappings;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoleMappingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var items = await _roleMappings.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RoleMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _roleMappings.GetByIdAsync(id);
        if (item == null) return NotFound();

        return Ok(item);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoleMappingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] RoleMappingCreateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _roleMappings.CreateAsync(request);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(RoleMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] RoleMappingUpdateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _roleMappings.UpdateAsync(id, request);

        if (updated == null) return NotFound();

        return Ok(updated);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _roleMappings.DeleteAsync(id);
        if (!deleted) return NotFound();

        return NoContent();
    }
}
