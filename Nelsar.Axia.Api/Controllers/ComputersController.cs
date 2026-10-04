using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Api.Controllers;

/// <summary>
/// Computer account management, backed by the directory itself through
/// <see cref="IComputersModule"/>.
///
/// An account is addressed here by <c>id</c>, a hash of its distinguished name — stable without
/// anything being stored, and it changes when the account is renamed or moved, because the DN is
/// the entry's identity as far as the directory is concerned. A client holding an id across a
/// rename gets a 404 and should re-read the listing. Same contract as
/// <see cref="GroupsController"/> and <see cref="OrganizationalUnitsController"/>.
///
/// What these endpoints do is manage directory entries, which is not the same as managing
/// machines. Creating an account pre-stages an entry a machine can later claim when it joins the
/// domain; it does not join anything. Deleting one revokes a joined machine's trust, and that
/// machine has to rejoin before it can authenticate again. Renaming re-addresses the entry
/// without renaming the machine. The request DTOs say the same thing at the point of use.
///
/// A computer is an Active Directory concept: <c>computer</c> has no RFC 4519 equivalent, and
/// entries read from a directory that only knows <c>device</c> arrive without an operating
/// system or an enabled state, which is reported as null rather than guessed at.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class ComputersController : ControllerBase
{
    private readonly IComputersModule _computers;

    public ComputersController(IComputersModule computers)
    {
        _computers = computers;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ComputersRead)]
    [ProducesResponseType(typeof(PagedResponse<ComputerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? filter = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? pageToken = null)
    {
        var result = await _computers.SearchAsync(filter, pageSize, pageToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = Permissions.ComputersRead)]
    [ProducesResponseType(typeof(ComputerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _computers.GetByIdAsync(id);
        if (item == null)
            return NotFound();

        return Ok(item);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ComputersManage)]
    [ProducesResponseType(typeof(ComputerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateComputerRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _computers.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Permissions.ComputersManage)]
    [ProducesResponseType(typeof(ComputerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateComputerRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _computers.UpdateAsync(id, request);
        if (updated == null)
            return NotFound();

        return Ok(updated);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = Permissions.ComputersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _computers.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
