using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Api.Authorization;
using Nelsar.Axia.Api.Configuration;
using Nelsar.Axia.Api.Middleware;
using Nelsar.Axia.Api.Resources;
using Microsoft.AspNetCore.RateLimiting;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Infrastructure.RateLimiting;

namespace Nelsar.Axia.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth-login")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class AuthController : ControllerBase
{
    private readonly IAuthModule _auth;
    private readonly AuthOptions _options;
    private readonly SignInThrottle _throttle;
    private readonly IRateLimitSettingsProvider _rateLimits;

    public AuthController(
        IAuthModule auth,
        IOptions<AuthOptions> options,
        SignInThrottle throttle,
        IRateLimitSettingsProvider rateLimits)
    {
        _auth = auth;
        _options = options.Value;
        _throttle = throttle;
        _rateLimits = rateLimits;
    }

    /// <summary>
    /// Creates a directory account for an unauthenticated caller — and only where a deployment
    /// has asked for that, which none does by default. See <see cref="AuthOptions"/>.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Register([FromBody] UserCreateRequest request)
    {
        // 404 rather than 403, because as far as this deployment is concerned the endpoint is not
        // there. A 403 would confirm to anyone probing that self-registration exists and is merely
        // switched off, which is an invitation to come back when it is not.
        if (!_options.AllowSelfRegistration)
            return NotFound(new { message = ApiMessages.SelfRegistrationDisabled() });

        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Domain failures (malformed value -> 400, already exists -> 409) are mapped centrally
        // by GlobalExceptionHandler, so there is nothing to catch here.
        var user = await _auth.RegisterAsync(request);
        return CreatedAtAction(nameof(Register), new { id = user.Id }, user);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthenticateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] AuthenticateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Per account, on top of the per-address limit the attribute applies; see SignInThrottle.
        var directory = HttpContext.Items[ProviderHeaderMiddleware.ItemsKey] as string;
        var limits = _rateLimits.GetSettings(SignInThrottle.PolicyName);
        if (_throttle.Blocked(request.Email, directory, limits) is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ApiMessages.TooManyRequests() });
        }

        var response = await _auth.LoginAsync(request);
        if (response == null)
        {
            // Refused credentials only. A directory that could not be asked throws instead, and
            // an outage is not counted against the people trying to sign in through it.
            _throttle.RecordFailure(request.Email, directory, limits);
            return Unauthorized(new { message = ApiMessages.InvalidCredentials() });
        }

        _throttle.Reset(request.Email, directory);
        return Ok(response);
    }
}
