using Microsoft.AspNetCore.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class UsersController : ControllerBase
{
    private readonly IUsersModule _users;

    public UsersController(IUsersModule users)
    {
        _users = users;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.UsersList)]
    [ProducesResponseType(typeof(UserListingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var users = await _users.GetAllAsync();
        return Ok(users);
    }

    [HttpPost("bulk")]
    [Authorize(Policy = Permissions.UsersBulk)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Bulk([FromBody] IEnumerable<BulkUserRequest> requests)
    {
        // An empty batch is refused by the command's validator, which answers 400 with the same
        // message this action used to write itself — see EnqueueBulkUserJobCommandValidator.
        var jobId = await _users.EnqueueBulkAsync(requests);
        return AcceptedAtAction(nameof(GetBulkJobStatus), new { jobId }, new { jobId });
    }

    /// <summary>
    /// Processes a small bulk batch immediately (no background job) and reports a
    /// per-item result via HTTP 207 Multi-Status, since the batch can be a mix of
    /// per-object successes and failures.
    /// </summary>
    [HttpPost("bulk/sync")]
    [Authorize(Policy = Permissions.UsersBulk)]
    [ProducesResponseType(typeof(BulkOperationResponse<BulkUserResult>), StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkSync([FromBody] IEnumerable<BulkUserRequest> requests)
    {
        var result = await _users.ProcessBulkAsync(requests);
        return StatusCode(StatusCodes.Status207MultiStatus, result);
    }

    [HttpGet("bulk/status/{jobId}")]
    [Authorize(Policy = Permissions.UsersBulk)]
    [ProducesResponseType(typeof(BulkOperationResponse<BulkUserResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBulkJobStatus(string jobId)
    {
        var status = await _users.GetBulkJobStatusAsync(jobId);
        if (status == null) return NotFound();

        return Ok(status);
    }

    [HttpPost("generate")]
    [Authorize(Policy = Permissions.UsersGenerate)]
    [ProducesResponseType(typeof(PatternGeneratorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] PatternGeneratorRequest request)
    {
        // A blank pattern is refused by GenerateUserPatternsQueryValidator, with the message
        // this action used to write itself.
        var result = await _users.GeneratePatternsAsync(request);

        return Ok(result);
    }

    [HttpGet("search")]
    [Authorize(Policy = Permissions.UsersRead)]
    [ProducesResponseType(typeof(PagedResponse<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string? filter = null, [FromQuery] int pageSize = 50, [FromQuery] string? pageToken = null)
    {
        var users = await _users.SearchAsync(filter, pageSize, pageToken);
        return Ok(users);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = Permissions.UsersRead)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null) return NotFound();

        return Ok(user);
    }

    /// <summary>
    /// Every attribute the account's directory entry carries, under the directory's own names —
    /// a separate read from <see cref="GetById"/>, which returns only the fields the application
    /// itself uses. Credential attributes are listed by name with their values withheld.
    /// </summary>
    [HttpGet("{id}/attributes")]
    [Authorize(Policy = Permissions.UsersRead)]
    [ProducesResponseType(typeof(UserAttributesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttributes(Guid id)
    {
        var attributes = await _users.GetAttributesAsync(id);
        if (attributes == null) return NotFound();

        return Ok(attributes);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.UsersCreate)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] UserCreateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Domain failures (malformed value -> 400, already exists -> 409) are mapped centrally
        // by GlobalExceptionHandler, so there is nothing to catch here.
        var user = await _users.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = Permissions.UsersUpdate)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UserUpdateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Directory failures now carry their own status (404 / 502) and are mapped centrally
        // by GlobalExceptionHandler.
        var user = await _users.UpdateAsync(id, request);
        if (user == null) return NotFound();

        return Ok(user);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = Permissions.UsersDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _users.DeleteAsync(id);
        if (!deleted) return NotFound();

        return NoContent();
    }

}
