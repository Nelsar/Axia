using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Interfaces;

public interface ILdapService
{
    /// <summary>
    /// Checks the password and returns the DN of the entry it was checked against — null when
    /// the directory rejected it, or found nobody for the login.
    ///
    /// The DN rather than a yes or no, because everything the session does afterwards has to be
    /// about that entry. A login is a value of mail, uid, sAMAccountName or userPrincipalName,
    /// which two entries can share; looking it up again after the bind could find the other one.
    /// </summary>
    Task<string?> AuthenticateAsync(string username, string password);

    /// <summary>
    /// The first entry the login matches. For lookups by what a person types or an address a
    /// caller holds — not for deciding who somebody is; see <see cref="GetUserByDnAsync"/>.
    /// </summary>
    Task<LdapUserInfo?> GetUserDetailsAsync(string username);

    /// <summary>
    /// The entry at exactly this DN, group memberships included, or null when there is none. Read
    /// rather than searched for, so it cannot be any other entry.
    /// </summary>
    Task<LdapUserInfo?> GetUserByDnAsync(string distinguishedName);

    /// <summary>Every attribute of one account's entry, or null when nothing matched. Values of
    /// credential attributes are never read; the attribute names still are.</summary>
    Task<DirectoryEntryAttributes?> GetUserAttributesAsync(string username);
    Task<DirectoryUserListing> GetAllUsersAsync();
    Task<DirectoryUserListing> SearchUsersAsync(string ldapFilter);
    Task CreateUserAsync(string name, string email, string password);

    /// <summary>
    /// Changes the entry at exactly this DN. Addressed by DN, not by login, for the same reason
    /// as <see cref="GetUserByDnAsync"/>: the caller has already decided which account this is —
    /// and checked that they may change it — and a second lookup by address could land on
    /// another entry that shares the address.
    /// </summary>
    /// <exception cref="Nelsar.Axia.Domain.Exceptions.DomainNotFoundException">No entry has that DN.</exception>
    Task UpdateUserAsync(string distinguishedName, string? newName, string? newEmail, string? newPassword);

    /// <summary>Deletes the entry at exactly this DN; false when there is none.</summary>
    Task<bool> DeleteUserAsync(string distinguishedName);

    /// <summary>The single role reported as UserDto.Role, from the entry's own group DNs.
    /// Authorisation uses permissions.</summary>
    Task<string> GetUserRoleAsync(LdapUserInfo entry);

    /// <summary>Every organizational unit under the configured base DN, carrying whether the
    /// enumeration finished — absence from a truncated listing proves nothing.</summary>
    Task<DirectoryOrganizationalUnitListing> GetOrganizationalUnitsAsync();

    /// <summary>Returns the DN of the entry that was written.</summary>
    Task<string> CreateOrganizationalUnitAsync(string name, string parentDn, string description);

    /// <summary>Applies only the non-null parts and returns the DN the entry ends up at;
    /// renaming or moving re-addresses the entry and everything beneath it.</summary>
    Task<string> UpdateOrganizationalUnitAsync(string dn, string? newName, string? newParentDn, string? newDescription);

    /// <summary>False only when there was no such entry.</summary>
    Task<bool> DeleteOrganizationalUnitAsync(string dn);

    /// <summary>What renaming or moving the container at <paramref name="dn"/> would carry along.</summary>
    Task<DirectorySubtree> InspectSubtreeAsync(string dn);

    /// <summary>Every group under the configured base DN, carrying whether the enumeration
    /// finished — absence from a truncated listing proves nothing. One account's membership is
    /// on its entry instead; see <see cref="GetUserByDnAsync"/>.</summary>
    Task<DirectoryGroupListing> GetGroupsAsync();

    /// <summary>Who one group lists as members; see <c>ILdapProvider.GetGroupMembers</c>.</summary>
    Task<DirectoryGroupMembers> GetGroupMembersAsync(string groupDn);

    /// <summary>Returns the DN of the entry that was written. <paramref name="memberDns"/> must
    /// hold at least one member — see <c>ILdapProvider.CreateGroup</c>.</summary>
    Task<string> CreateGroupAsync(string name, string parentDn, string description, IReadOnlyList<string> memberDns);

    /// <summary>Applies only the non-null parts and returns the DN the entry ends up at. A null
    /// <paramref name="memberDns"/> leaves the membership alone; a non-null one replaces it
    /// wholesale and may not be empty.</summary>
    Task<string> UpdateGroupAsync(string dn, string? newName, string? newParentDn, string? newDescription, IReadOnlyList<string>? memberDns);

    /// <summary>False only when there was no such entry.</summary>
    Task<bool> DeleteGroupAsync(string dn);

    /// <summary>Every computer account under the configured base DN, carrying whether the
    /// enumeration finished — absence from a truncated listing proves nothing.</summary>
    Task<DirectoryComputerListing> GetComputersAsync();

    /// <summary>Returns the DN of the entry that was written. Pre-stages an account; the machine
    /// still joins the domain itself.</summary>
    Task<string> CreateComputerAsync(string name, string parentDn, string description, string dnsHostName);

    /// <summary>Applies only the non-null parts and returns the DN the entry ends up at.</summary>
    Task<string> UpdateComputerAsync(string dn, string? newName, string? newParentDn, string? newDescription, string? newDnsHostName, bool? enabled);

    /// <summary>False only when there was no such entry.</summary>
    Task<bool> DeleteComputerAsync(string dn);
}
