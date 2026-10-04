using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Identity;

namespace Nelsar.Axia.Infrastructure.Identity;

internal sealed class JwtTokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtTokenService(IOptions<JwtSettings> jwtOptions, IHttpContextAccessor httpContextAccessor)
    {
        _jwtSettings = jwtOptions.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public string GenerateToken(UserDto user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),

            // The value the directory can actually be searched by, carried explicitly rather
            // than left for the reader to pick out of the other claims. Sub is a derived Guid
            // that matches no directory attribute, and Email is empty whenever the entry has no
            // mail — so neither is dependable as the thing authorisation looks an account up by.
            new(JwtRegisteredClaimNames.PreferredUsername, user.DirectoryAddress),

            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role)
        };

        // Which directory this session belongs to, taken from the request that is signing in.
        //
        // Read off the ambient request rather than threaded through ITokenService, for the same
        // reason LdapService reads it the same way: it is a property of the request being served,
        // not of the user being described, and UserDto has no business carrying it. Nothing mints
        // a token outside a request today; if something ever does, it names no directory and the
        // session is bound to the default — which is the safe end of that choice.
        //
        // Binding matters because everything downstream is decided per directory: which entry the
        // login resolves to, which groups it is in, which roles those map to. A session that left
        // the directory to a per-request header let a caller keep the identity proven against one
        // domain while pointing the operation at another. See ProviderHeaderMiddleware, which is
        // where this claim is enforced.
        var directory = _httpContextAccessor.HttpContext?.Items["LdapProviderId"] as string;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            claims.Add(new Claim(AxiaClaims.LdapProviderId, directory.Trim()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiresInMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
