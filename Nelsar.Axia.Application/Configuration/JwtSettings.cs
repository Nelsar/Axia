namespace Nelsar.Axia.Application.Configuration;

public class JwtSettings
{
    /// <summary>What Issuer and Audience are when configuration does not say.</summary>
    public const string DefaultIssuerAndAudience = "Nelsar.Axia.Api";

    public const int DefaultExpiresInMinutes = 60;

    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Written into every token and required of every token on the way back in, so the two have
    /// to agree — which a default guarantees. These used to default to empty, and an instance
    /// configured only through the environment, as a container is, issued tokens that its own
    /// validation then refused: signing in worked and every request after it was answered 401.
    /// </summary>
    public string Issuer { get; set; } = DefaultIssuerAndAudience;

    /// <inheritdoc cref="Issuer"/>
    public string Audience { get; set; } = DefaultIssuerAndAudience;

    /// <summary>
    /// How long a token lives. Used to default to 0, which issued tokens already expired — usable
    /// only for the five minutes of clock skew validation allows — while the README promised 60.
    /// </summary>
    public int ExpiresInMinutes { get; set; } = DefaultExpiresInMinutes;
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string UserCreator = "UserCreator";
    public const string UserEditor = "UserEditor";
    public const string UserDeleter = "UserDeleter";
    public const string User = "User";

    /// <summary>
    /// Every role, most privileged first — the order they are listed in for editing. The set is
    /// fixed: a role is something a role mapping can name and the console can offer, and a name
    /// typed into the grants table alone would be neither.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Admin, UserCreator, UserEditor, UserDeleter, User];

    /// <summary>The role's own spelling, or null when no role goes by that name.</summary>
    public static string? Canonical(string? roleName) =>
        All.FirstOrDefault(role => string.Equals(role, roleName?.Trim(), StringComparison.OrdinalIgnoreCase));
}
