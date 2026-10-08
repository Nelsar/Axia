using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Mints JWTs signed with the key ApiTestFactory injects into the host configuration, for the
/// issuer and audience the host defaults to, so tests can exercise [Authorize]-protected
/// endpoints end to end.
/// </summary>
public static class TestAuth
{
    public const string SigningKey = "test-signing-key-0123456789ABCDEF";

    /// <summary>A valid 32-byte AES key, base64-encoded, for Encryption:Key.</summary>
    public static readonly string EncryptionKey = Convert.ToBase64String(new byte[32]);
    /// <summary>The application's own defaults: ApiTestFactory leaves both unset.</summary>
    public const string Issuer = JwtSettings.DefaultIssuerAndAudience;

    /// <inheritdoc cref="Issuer"/>
    public const string Audience = JwtSettings.DefaultIssuerAndAudience;

    /// <param name="preferredUsername">
    /// What the real JwtTokenService puts in preferred_username: the address the directory is
    /// searched by. Defaults to <paramref name="email"/>, which is what it resolves to for any
    /// account that has a mail attribute. Pass it explicitly to mint the token an account
    /// without one gets — empty email, login here.
    /// </param>
    /// <param name="providerId">
    /// The directory this session is opened against, as the real JwtTokenService records it from
    /// the sign-in request. Leaving it null mints a session on the default directory — which is
    /// what most tests want, and which ProviderHeaderMiddleware will refuse to let carry an
    /// X-LDAP-Provider-Id header naming anything else.
    /// </param>
    /// <summary>The entry a test principal is taken to be, when a test does not name one.</summary>
    public static string DistinguishedNameFor(string login) => $"CN={login},OU=Users,DC=example,DC=com";

    public static string GenerateToken(
        string role,
        string email = "test@example.com",
        string name = "Test User",
        Guid? userId = null,
        string? preferredUsername = null,
        Guid? providerId = null,
        string? distinguishedName = null,
        bool includeDistinguishedName = true)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, (userId ?? Guid.NewGuid()).ToString()),
            new(JwtRegisteredClaimNames.PreferredUsername, preferredUsername ?? email),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role)
        };

        // What every token the service mints carries; leaving it out builds the kind of token
        // issued before the claim existed, which the API now refuses.
        if (includeDistinguishedName)
        {
            claims.Add(new Claim(AxiaClaims.DistinguishedName,
                distinguishedName ?? DistinguishedNameFor(preferredUsername ?? email)));
        }

        if (providerId is not null)
        {
            claims.Add(new Claim(AxiaClaims.LdapProviderId, providerId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
