using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Nelsar.Axia.Contracts.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Mints JWTs signed with the same key/issuer/audience ApiTestFactory injects into the
/// host configuration, so tests can exercise [Authorize]-protected endpoints end to end.
/// </summary>
public static class TestAuth
{
    public const string SigningKey = "test-signing-key-0123456789ABCDEF";

    /// <summary>A valid 32-byte AES key, base64-encoded, for Encryption:Key.</summary>
    public static readonly string EncryptionKey = Convert.ToBase64String(new byte[32]);
    public const string Issuer = "Nelsar.Axia.Tests";
    public const string Audience = "Nelsar.Axia.Tests";

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
    public static string GenerateToken(
        string role,
        string email = "test@example.com",
        string name = "Test User",
        Guid? userId = null,
        string? preferredUsername = null,
        Guid? providerId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, (userId ?? Guid.NewGuid()).ToString()),
            new(JwtRegisteredClaimNames.PreferredUsername, preferredUsername ?? email),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role)
        };

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
