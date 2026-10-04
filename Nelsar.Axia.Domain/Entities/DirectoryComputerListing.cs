namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// The outcome of enumerating computer accounts, together with whether that enumeration
/// finished — the same contract as <see cref="DirectoryGroupListing"/>, for the same reason: a
/// paged LDAP search can stop partway through, and a short list that looks complete is how "we
/// could not read the directory" gets mistaken for "the directory holds no more".
///
/// A computer's identity is resolved by scanning this listing, so not finding an id in a
/// truncated enumeration says nothing about whether the account exists. Callers must not turn
/// that into a 404.
/// </summary>
public sealed record DirectoryComputerListing(
    IReadOnlyList<LdapComputer> Computers,
    bool IsComplete)
{
    /// <summary>The enumeration ran to the end; the set is everything the filter matched.</summary>
    public static DirectoryComputerListing Complete(IReadOnlyList<LdapComputer> computers) => new(computers, true);

    /// <summary>The enumeration was cut short; absence from <paramref name="computers"/> proves nothing.</summary>
    public static DirectoryComputerListing Truncated(IReadOnlyList<LdapComputer> computers) => new(computers, false);
}
