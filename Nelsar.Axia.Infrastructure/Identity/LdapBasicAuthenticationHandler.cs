using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Interfaces;

namespace Nelsar.Axia.Infrastructure.Identity;

internal sealed class LdapBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ILdapService _ldapService;

    public LdapBasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ILdapService ldapService)
        : base(options, logger, encoder)
    {
        _ldapService = ldapService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeaderValues))
        {
            return AuthenticateResult.NoResult();
        }

        var authorizationHeader = authorizationHeaderValues.ToString();
        if (!authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var encodedCredentials = authorizationHeader["Basic ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(encodedCredentials))
        {
            return AuthenticateResult.Fail("Missing LDAP credentials.");
        }

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Invalid Base64 credentials.");
        }

        var separatorIndex = credentials.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return AuthenticateResult.Fail("Invalid LDAP credentials format.");
        }

        var username = credentials[..separatorIndex];
        var password = credentials[(separatorIndex + 1)..];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return AuthenticateResult.Fail("LDAP username or password is empty.");
        }

        var isAuthenticated = await _ldapService.AuthenticateAsync(username, password);
        if (!isAuthenticated)
        {
            return AuthenticateResult.Fail("Invalid LDAP credentials.");
        }

        var details = await _ldapService.GetUserDetailsAsync(username);
        var role = await _ldapService.GetUserRoleAsync(username);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(details?.DisplayName) ? username : details.DisplayName),
            new(ClaimTypes.Email, string.IsNullOrWhiteSpace(details?.Email) ? username : details.Email),
            new(ClaimTypes.Role, role)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["WWW-Authenticate"] = "Basic realm=\"LDAP\"";
        return base.HandleChallengeAsync(properties);
    }
}