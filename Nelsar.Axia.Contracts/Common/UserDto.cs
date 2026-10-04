namespace Nelsar.Axia.Contracts.Common;

public class UserDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// The account's mail attribute, and only that. Empty when the entry carries none — the
    /// login used to stand in here, which meant a field labelled "email" could hold a cn and
    /// nothing told the caller which it was looking at. <see cref="Username"/> is where the
    /// login lives now.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The login as the directory reports it (uid / sAMAccountName / userPrincipalName,
    /// falling back to cn).</summary>
    public string Username { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>The entry's distinguished name — its identity in the directory, and the form
    /// groups and organizational units are addressed by.</summary>
    public string Dn { get; set; } = string.Empty;

    /// <summary>
    /// Group DNs from the entry's <c>memberOf</c>. Empty means "no memberships were reported",
    /// which is not the same as "belongs to no groups": OpenLDAP only populates memberOf with
    /// the memberof overlay enabled, so a directory without it reports nothing here.
    /// </summary>
    public string[] Groups { get; set; } = [];

    /// <summary>
    /// The value this account is addressed by when talking to the directory, and the one its
    /// <see cref="Id"/> derives from — the mail attribute when there is one, the login
    /// otherwise. Both match the portable user search filter, so either reaches the entry.
    ///
    /// Not part of the response: it exists so that making <see cref="Email"/> honest did not
    /// also change every derived id and break the lookup index, which keys on this value.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DirectoryAddress =>
        string.IsNullOrWhiteSpace(Email) ? Username : Email;
}
