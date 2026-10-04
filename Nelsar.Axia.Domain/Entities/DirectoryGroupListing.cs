namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// The outcome of enumerating groups, together with whether that enumeration actually finished
/// — the same contract as <see cref="DirectoryOrganizationalUnitListing"/>, for the same reason:
/// a paged LDAP search can stop partway through, and a short list that looks complete is how
/// "we could not read the directory" gets mistaken for "the directory holds no more".
///
/// A group's identity is resolved by scanning this listing, so not finding an id in a truncated
/// enumeration says nothing about whether the group exists. Callers must not turn that into a 404.
/// </summary>
public sealed record DirectoryGroupListing(
    IReadOnlyList<LdapGroup> Groups,
    bool IsComplete)
{
    /// <summary>The enumeration ran to the end; the set is everything the filter matched.</summary>
    public static DirectoryGroupListing Complete(IReadOnlyList<LdapGroup> groups) => new(groups, true);

    /// <summary>The enumeration was cut short; absence from <paramref name="groups"/> proves nothing.</summary>
    public static DirectoryGroupListing Truncated(IReadOnlyList<LdapGroup> groups) => new(groups, false);
}
