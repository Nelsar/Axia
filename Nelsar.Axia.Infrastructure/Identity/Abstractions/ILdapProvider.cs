using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Identity.Abstractions;

/// <summary>
/// The LDAP provider abstraction. Implemented by:
/// - StandardLdapProvider (Novell.Directory.Ldap — cross-platform, OpenLDAP)
/// - ActiveDirectoryProvider (System.DirectoryServices.AccountManagement — Windows AD)
/// </summary>
internal interface ILdapProvider
{
    /// <summary>
    /// The DN of the entry the password was proven against, or null when it was rejected or the
    /// login named nobody. Outages are exceptions, never null.
    /// </summary>
    string? Authenticate(string username, string password);

    LdapUserInfo? FindUser(string username);

    /// <summary>
    /// The entry at exactly <paramref name="distinguishedName"/>, its group DNs included, or null
    /// when there is no such entry. Read, not searched for, so it cannot be another entry.
    /// </summary>
    LdapUserInfo? FindUserByDn(string distinguishedName);

    DirectoryUserListing GetAllUsers();

    /// <summary>
    /// Every attribute of one account's entry, or null when no entry matched — the same
    /// "searched and found nobody" that <see cref="FindUser"/> means by null, never a failed
    /// search. Values of secret attributes are not read; see <see cref="DirectoryAttribute"/>.
    /// </summary>
    DirectoryEntryAttributes? GetUserAttributes(string username);

    void CreateUser(string name, string email, string password);

    // An account is changed and deleted by DN, like every other entry here: whoever calls has
    // already decided which entry it is, and a login looked up again can match a different one.

    /// <exception cref="Nelsar.Axia.Domain.Exceptions.DomainNotFoundException">No entry has that DN.</exception>
    void UpdateUser(string distinguishedName, string? newName, string? newEmail, string? newPassword);

    /// <summary>False when there was no such entry; refusals are exceptions, never false.</summary>
    bool DeleteUser(string distinguishedName);

    // Organizational units. Addressed by DN rather than by name: an OU name is unique only
    // among its siblings, so "Finance" identifies an entry no better than a bare file name
    // identifies a file. Callers holding an API-level id resolve it against the enumeration.

    DirectoryOrganizationalUnitListing GetAllOrganizationalUnits();

    /// <summary>Returns the DN of the entry that was written. Only the provider knows what a
    /// blank parent resolves to, so the caller is told rather than left to guess.</summary>
    string CreateOrganizationalUnit(string name, string parentDn, string description);

    /// <summary>
    /// Applies only the parts that are non-null, and returns the DN the entry ends up at. A
    /// changed <paramref name="newName"/> is an RDN change and a changed
    /// <paramref name="newParentDn"/> a move — both are moddn operations that re-address the
    /// entry and every entry beneath it, which is why they are stated explicitly here rather
    /// than inferred from a rewritten DN.
    /// </summary>
    string UpdateOrganizationalUnit(string dn, string? newName, string? newParentDn, string? newDescription);

    /// <summary>False when there was no such entry. Refusals — a unit that still has children,
    /// a directory that will not permit the delete — are exceptions, never false.</summary>
    bool DeleteOrganizationalUnit(string dn);

    // Groups. Addressed by DN for the same reason organizational units are: a group name is
    // unique only within its container. Membership is a list of member DNs — what the directory
    // actually stores — and never logins.

    DirectoryGroupListing GetAllGroups();

    /// <summary>
    /// Who the group at exactly <paramref name="groupDn"/> lists as members, or
    /// <see cref="DirectoryGroupMembers.None"/> when there is no such group. Read, not searched
    /// for. A directory that pages a large member list — Active Directory past 1,500 members —
    /// may return only part of it; the accounts' own memberOf covers that case.
    /// </summary>
    DirectoryGroupMembers GetGroupMembers(string groupDn);

    /// <summary>
    /// Returns the DN of the entry that was written. <paramref name="memberDns"/> must hold at
    /// least one member: the entry is a <c>groupOfNames</c>, whose schema makes <c>member</c>
    /// mandatory, so an empty group is not a state the directory can hold.
    /// </summary>
    string CreateGroup(string name, string parentDn, string description, IReadOnlyList<string> memberDns);

    /// <summary>
    /// Applies only the parts that are non-null, and returns the DN the entry ends up at — a
    /// changed name is an RDN change and a changed parent a move, both moddn operations, exactly
    /// as for <see cref="UpdateOrganizationalUnit"/>.
    ///
    /// A null <paramref name="memberDns"/> leaves the membership untouched; a non-null one
    /// replaces it and may not be empty, for the schema reason given on
    /// <see cref="CreateGroup"/>.
    /// </summary>
    string UpdateGroup(string dn, string? newName, string? newParentDn, string? newDescription, IReadOnlyList<string>? memberDns);

    /// <summary>False when there was no such entry; refusals are exceptions, never false.</summary>
    bool DeleteGroup(string dn);

    // Computer accounts. Addressed by DN like groups and organizational units. Note what a
    // write here does and does not do: the directory entry is the account, but a machine's
    // membership of the domain is established by the machine itself at join time. Creating an
    // entry pre-stages an account; deleting one revokes a joined machine's trust and obliges it
    // to rejoin; renaming re-addresses the entry without renaming the machine.

    DirectoryComputerListing GetAllComputers();

    /// <summary>Returns the DN of the entry that was written.</summary>
    string CreateComputer(string name, string parentDn, string description, string dnsHostName);

    /// <summary>
    /// Applies only the parts that are non-null and returns the DN the entry ends up at — a
    /// changed name is an RDN change and a changed parent a move, both moddn operations, exactly
    /// as for <see cref="UpdateGroup"/>. A non-null <paramref name="enabled"/> toggles the
    /// ACCOUNTDISABLE bit of userAccountControl.
    /// </summary>
    string UpdateComputer(string dn, string? newName, string? newParentDn, string? newDescription, string? newDnsHostName, bool? enabled);

    /// <summary>False when there was no such entry; refusals are exceptions, never false.</summary>
    bool DeleteComputer(string dn);
}
