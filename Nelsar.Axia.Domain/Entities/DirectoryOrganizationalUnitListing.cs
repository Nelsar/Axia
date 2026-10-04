namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// The outcome of enumerating organizational units, together with whether that enumeration
/// actually finished — the same contract as <see cref="DirectoryUserListing"/>, for the same
/// reason: a paged LDAP search can stop partway through, and a short list that looks complete
/// is how "we could not read the directory" gets mistaken for "the directory holds no more".
///
/// It matters more here than for users, because a unit's identity is resolved by scanning this
/// listing. Not finding an id in a truncated enumeration says nothing about whether the unit
/// exists, so callers must not turn that into a 404.
/// </summary>
public sealed record DirectoryOrganizationalUnitListing(
    IReadOnlyList<LdapOrganizationalUnit> Units,
    bool IsComplete)
{
    /// <summary>The enumeration ran to the end; the set is everything the filter matched.</summary>
    public static DirectoryOrganizationalUnitListing Complete(IReadOnlyList<LdapOrganizationalUnit> units) =>
        new(units, true);

    /// <summary>The enumeration was cut short; absence from <paramref name="units"/> proves nothing.</summary>
    public static DirectoryOrganizationalUnitListing Truncated(IReadOnlyList<LdapOrganizationalUnit> units) =>
        new(units, false);
}
