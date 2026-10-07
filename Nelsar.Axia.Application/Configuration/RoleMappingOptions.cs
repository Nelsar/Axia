namespace Nelsar.Axia.Application.Configuration;

/// <summary>
/// How role mappings are bootstrapped, and whether mappings that name a group by CN alone count.
/// </summary>
public sealed class RoleMappingOptions
{
    public const string SectionName = "RoleMappings";

    /// <summary>
    /// The group, by distinguished name, that each role is granted to on first run — and that an
    /// existing mapping still written by name for that role is converted to on the next start.
    /// Keyed by role: Admin, UserCreator, UserEditor, UserDeleter.
    ///
    /// Admin is the one that matters: without it a fresh database has nobody who can sign in and
    /// configure the rest, and the application refuses to start rather than guess (see
    /// <see cref="AllowMappingsByName"/>).
    /// </summary>
    public Dictionary<string, string> GroupDns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a mapping that names its group by CN alone grants its role. Off by default: such a
    /// mapping grants the role to every group of that name anywhere in the directory, so anyone
    /// who can create a group can create an "Administrators" of their own.
    ///
    /// On keeps the behaviour of the builds before DN mappings, for a deployment that cannot yet
    /// say where its groups live. It is a known hole while it is on.
    /// </summary>
    public bool AllowMappingsByName { get; set; }
}
