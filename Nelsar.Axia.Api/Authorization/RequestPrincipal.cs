using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Nelsar.Axia.Api.Authorization;

/// <summary>
/// Reads, from the current request, the two things authorisation is decided on: who is asking and
/// which directory they authenticated against.
///
/// Shared by <see cref="PermissionAuthorizationHandler"/> (the edge check) and
/// <see cref="HttpCurrentUser"/> (what the Application layer's own check sees). Two copies of this
/// would be two answers to "who is the caller", and the interesting failure is not that they
/// disagree loudly — it is that one grants where the other would refuse.
/// </summary>
internal static class RequestPrincipal
{
    /// <summary>
    /// The login to look the principal up by.
    ///
    /// preferred_username first: the token states outright what the directory can be searched
    /// by. Email used to lead here, and worked only because it was quietly filled with the login
    /// whenever an account had no mail attribute. Once it stopped being, this fell through to
    /// NameIdentifier — which for a JWT is <c>sub</c> mapped inbound, i.e. a derived Guid. Every
    /// request from such an account then asked the directory for an entry named by a Guid, and
    /// failed the whole request with 502.
    ///
    /// Email and NameIdentifier stay as fallbacks for tokens minted before preferred_username was
    /// added to them.
    /// </summary>
    public static string? Subject(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var candidates = new[]
        {
            user.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername),
            user.FindFirstValue(ClaimTypes.Email),
            user.FindFirstValue(JwtRegisteredClaimNames.Email),
            user.FindFirstValue(ClaimTypes.NameIdentifier),
        };

        return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// The directory named by the optional provider header, as captured by
    /// <c>ProviderHeaderMiddleware</c>. Null means the configured default.
    /// </summary>
    public static Guid? ProviderId(HttpContext? httpContext)
    {
        var id = httpContext?.Items["LdapProviderId"] as string;
        return Guid.TryParse(id, out var providerId) ? providerId : null;
    }
}
