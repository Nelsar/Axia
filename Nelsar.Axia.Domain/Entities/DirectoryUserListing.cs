namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// The outcome of enumerating directory users, together with whether that enumeration
/// actually finished.
///
/// A paged LDAP search can fail partway through: the pages already read are real users, but
/// the set is no longer the whole directory. Returning just the list makes a truncated result
/// indistinguishable from a complete one — an operator sees a short list as the full picture,
/// and "this id is not in the directory" gets concluded from a search that never ran to the
/// end. Carrying <see cref="IsComplete"/> alongside the data is what lets each caller decide
/// whether a partial answer is good enough for what it is doing.
/// </summary>
public sealed record DirectoryUserListing(IReadOnlyList<LdapUserInfo> Users, bool IsComplete)
{
    /// <summary>The enumeration ran to the end; the set is everything the filter matched.</summary>
    public static DirectoryUserListing Complete(IReadOnlyList<LdapUserInfo> users) => new(users, true);

    /// <summary>The enumeration was cut short; absence from <paramref name="users"/> proves nothing.</summary>
    public static DirectoryUserListing Truncated(IReadOnlyList<LdapUserInfo> users) => new(users, false);
}
