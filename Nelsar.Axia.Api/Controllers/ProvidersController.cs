using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Nelsar.Axia.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Api.Controllers;

[ApiController]
[Route("api/v1/providers")]
[Authorize(Policy = Permissions.ProvidersManage)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class ProvidersController : ControllerBase
{
    /// <summary>
    /// Rate limit policy for the one endpoint here that anyone may call. Named where it is
    /// applied; the limits themselves are a DB row an admin can retune without a redeploy.
    /// </summary>
    public const string SelectableProvidersPolicy = "providers-selectable";

    private readonly IProvidersModule _providers;

    public ProvidersController(IProvidersModule providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// The directories a caller may sign in to — id and name only.
    ///
    /// Anonymous, because it has to be: a sign-in page reads it to fill the directory picker,
    /// before anyone has had credentials accepted. Until it existed, the id needed to sign in to
    /// anything but the default directory was only obtainable from the listing below, which needs
    /// providers.manage — so in practice only an administrator could reach a second domain.
    ///
    /// Everything else on this controller describes how a directory is reached, and stays behind
    /// that permission.
    ///
    /// The configured default directory is not in the list and has no id: signing in without the
    /// header is how it is chosen, so a client offering it alongside these should send no header
    /// rather than look for an entry here.
    /// </summary>
    [HttpGet("selectable")]
    [AllowAnonymous]
    [EnableRateLimiting(SelectableProvidersPolicy)]
    [ProducesResponseType(typeof(IEnumerable<SelectableProviderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSelectable(CancellationToken cancellationToken)
    {
        var items = await _providers.GetSelectableAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProviderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var items = await _providers.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _providers.GetByIdAsync(id);
        if (item == null) return NotFound();

        return Ok(item);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProviderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ProviderCreateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _providers.CreateAsync(request);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProviderUpdateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _providers.UpdateAsync(id, request);

        if (updated == null) return NotFound();

        return Ok(updated);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _providers.DeleteAsync(id);
        if (!deleted) return NotFound();

        return NoContent();
    }
}
