using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Api.Controllers;

/// <summary>
/// Group management, backed by the directory itself, through <see cref="IGroupsModule"/>.
///
/// A group is addressed here by <c>id</c>, which is a hash of its distinguished name — stable
/// without anything being stored, and it changes when the group is renamed or moved, because the
/// DN is the group's identity as far as the directory is concerned. A client holding an id across
/// a rename will get a 404 and should re-read the listing.
///
/// Membership is a list of member distinguished names, as the directory stores it. On update it
/// is replaced wholesale when given and left untouched when omitted.
///
/// This file barely changed when the feature was built: the actions always called the service and
/// the previous HTTP 501 came from the exception it raised. What did change is the listing, which
/// declared no <c>filter</c> parameter and passed null — so <c>?filter=</c> was accepted, dropped,
/// and answered with the whole directory. The same defect was fixed in
/// <see cref="OrganizationalUnitsController"/>.
/// </summary>
[ApiController]
[Route("api/v1/groups")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class GroupsController : ControllerBase
{
    private readonly IGroupsModule _groups;

    public GroupsController(IGroupsModule groups)
    {
        _groups = groups;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.GroupsRead)]
    [ProducesResponseType(typeof(PagedResponse<GroupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? filter = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? pageToken = null)
    {
        var result = await _groups.SearchAsync(filter, pageSize, pageToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = Permissions.GroupsRead)]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _groups.GetByIdAsync(id);
        if (item == null)
            return NotFound();

        return Ok(item);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.GroupsManage)]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _groups.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Permissions.GroupsManage)]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGroupRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _groups.UpdateAsync(id, request);
        if (updated == null)
            return NotFound();

        return Ok(updated);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = Permissions.GroupsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _groups.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
