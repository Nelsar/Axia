namespace Nelsar.Axia.Application.Configuration;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiresInMinutes { get; set; }
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
