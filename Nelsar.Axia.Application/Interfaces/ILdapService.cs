using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Interfaces;

public interface ILdapService
{
    Task<bool> AuthenticateAsync(string username, string password);
    Task<LdapUserInfo?> GetUserDetailsAsync(string username);

    /// <summary>Every attribute of one account's entry, or null when nothing matched. Values of
    /// credential attributes are never read; the attribute names still are.</summary>
    Task<DirectoryEntryAttributes?> GetUserAttributesAsync(string username);
    Task<DirectoryUserListing> GetAllUsersAsync();
    Task<DirectoryUserListing> SearchUsersAsync(string ldapFilter);
    Task CreateUserAsync(string name, string email, string password);
    Task UpdateUserAsync(string username, string? newName, string? newEmail, string? newPassword);
    Task<bool> DeleteUserAsync(string username);
    /// <summary>Raw group DNs from the directory. Throws when membership cannot be determined —
    /// an empty list means "in no groups", never "we could not find out".</summary>
    Task<IReadOnlyList<string>> GetUserGroupsAsync(string username);

    /// <summary>The single role reported as UserDto.Role. Authorisation uses permissions.</summary>
    Task<string> GetUserRoleAsync(string username);

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

    /// <summary>Every group under the configured base DN, carrying whether the enumeration
    /// finished — absence from a truncated listing proves nothing. Distinct from
    /// <see cref="GetUserGroupsAsync"/>, which reads one account's membership.</summary>
    Task<DirectoryGroupListing> GetGroupsAsync();

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
