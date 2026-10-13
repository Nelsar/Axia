using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;

namespace Nelsar.Axia.Infrastructure.Identity;

internal class LdapService : ILdapService
{
    private readonly ILdapProviderFactory _providerFactory;
    private readonly IRoleMappingResolver _roleMappingResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LdapService(
        ILdapProviderFactory providerFactory,
        IRoleMappingResolver roleMappingResolver,
        IHttpContextAccessor httpContextAccessor)
    {
        _providerFactory = providerFactory;
        _roleMappingResolver = roleMappingResolver;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<string?> AuthenticateAsync(string username, string password)
    {
        var provider = _providerFactory.Resolve();
        return Task.FromResult(provider.Authenticate(username, password));
    }

    public Task<LdapUserInfo?> GetUserDetailsAsync(string username)
    {
        var provider = _providerFactory.Resolve();
        return Task.FromResult(provider.FindUser(username));
    }

    public Task<LdapUserInfo?> GetUserByDnAsync(string distinguishedName)
    {
        var provider = _providerFactory.Resolve();
        return Task.FromResult(provider.FindUserByDn(distinguishedName));
    }

    public Task<DirectoryEntryAttributes?> GetUserAttributesAsync(string username)
    {
        var provider = _providerFactory.Resolve();
        return Task.FromResult(provider.GetUserAttributes(username));
    }

    public Task<DirectoryUserListing> GetAllUsersAsync()
    {
        var provider = _providerFactory.Resolve();
        return Task.FromResult(provider.GetAllUsers());
    }

    public Task<DirectoryUserListing> SearchUsersAsync(string ldapFilter)
    {
        if (string.IsNullOrWhiteSpace(ldapFilter))
            return GetAllUsersAsync();

        var searchTerms = ParseLdapSearchTokens(ldapFilter).ToArray();
        if (searchTerms.Length == 0)
            return GetAllUsersAsync();

        var provider = _providerFactory.Resolve();
        var listing = provider.GetAllUsers();
        var matched = listing.Users
            .Where(info => MatchesLdapSearchTokens(info, searchTerms))
            .ToList();

        // Filtering narrows the set but cannot make a truncated enumeration complete: a match
        // may well be sitting in the pages we never got to read.
        return Task.FromResult(listing with { Users = matched });
    }

    public Task CreateUserAsync(string name, string email, string password)
    {
        var provider = _providerFactory.Resolve();
        provider.CreateUser(name, email, password);
        return Task.CompletedTask;
    }

    public Task UpdateUserAsync(string distinguishedName, string? newName, string? newEmail, string? newPassword)
    {
        var provider = _providerFactory.Resolve();
        provider.UpdateUser(distinguishedName, newName, newEmail, newPassword);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteUserAsync(string distinguishedName) =>
        Task.FromResult(_providerFactory.Resolve().DeleteUser(distinguishedName));

    public Task<DirectoryOrganizationalUnitListing> GetOrganizationalUnitsAsync() =>
        Task.FromResult(_providerFactory.Resolve().GetAllOrganizationalUnits());

    public Task<string> CreateOrganizationalUnitAsync(string name, string parentDn, string description) =>
        Task.FromResult(_providerFactory.Resolve().CreateOrganizationalUnit(name, parentDn, description));

    public Task<string> UpdateOrganizationalUnitAsync(string dn, string? newName, string? newParentDn, string? newDescription) =>
        Task.FromResult(_providerFactory.Resolve().UpdateOrganizationalUnit(dn, newName, newParentDn, newDescription));

    public Task<bool> DeleteOrganizationalUnitAsync(string dn) =>
        Task.FromResult(_providerFactory.Resolve().DeleteOrganizationalUnit(dn));

    public Task<DirectorySubtree> InspectSubtreeAsync(string dn) =>
        Task.FromResult(_providerFactory.Resolve().InspectSubtree(dn));

    public Task<DirectoryGroupListing> GetGroupsAsync() =>
        Task.FromResult(_providerFactory.Resolve().GetAllGroups());

    public Task<DirectoryGroupMembers> GetGroupMembersAsync(string groupDn) =>
        Task.FromResult(_providerFactory.Resolve().GetGroupMembers(groupDn));

    public Task<string> CreateGroupAsync(string name, string parentDn, string description, IReadOnlyList<string> memberDns) =>
        Task.FromResult(_providerFactory.Resolve().CreateGroup(name, parentDn, description, memberDns));

    public Task<string> UpdateGroupAsync(string dn, string? newName, string? newParentDn, string? newDescription, IReadOnlyList<string>? memberDns) =>
        Task.FromResult(_providerFactory.Resolve().UpdateGroup(dn, newName, newParentDn, newDescription, memberDns));

    public Task<bool> DeleteGroupAsync(string dn) =>
        Task.FromResult(_providerFactory.Resolve().DeleteGroup(dn));

    public Task<DirectoryComputerListing> GetComputersAsync() =>
        Task.FromResult(_providerFactory.Resolve().GetAllComputers());

    public Task<string> CreateComputerAsync(string name, string parentDn, string description, string dnsHostName) =>
        Task.FromResult(_providerFactory.Resolve().CreateComputer(name, parentDn, description, dnsHostName));

    public Task<string> UpdateComputerAsync(
        string dn, string? newName, string? newParentDn, string? newDescription, string? newDnsHostName, bool? enabled) =>
        Task.FromResult(_providerFactory.Resolve()
            .UpdateComputer(dn, newName, newParentDn, newDescription, newDnsHostName, enabled));

    public Task<bool> DeleteComputerAsync(string dn) =>
        Task.FromResult(_providerFactory.Resolve().DeleteComputer(dn));

    public Task<string> GetUserRoleAsync(LdapUserInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _roleMappingResolver.ResolvePrimaryRoleAsync(entry.Groups, TryGetCurrentProviderId());
    }

    private Guid? TryGetCurrentProviderId()
    {
        var id = _httpContextAccessor.HttpContext?.Items["LdapProviderId"] as string;
        return Guid.TryParse(id, out var guid) ? guid : null;
    }

    private static IEnumerable<string> ParseLdapSearchTokens(string ldapFilter)
    {
        if (string.IsNullOrWhiteSpace(ldapFilter))
            return Array.Empty<string>();

        var matches = Regex.Matches(ldapFilter, @"\*(?<term>(?:\\.|[^*\\])*)\*");
        return matches.Select(match => UnescapeLdapFilterValue(match.Groups["term"].Value))
                      .Where(token => !string.IsNullOrWhiteSpace(token))
                      .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesLdapSearchTokens(LdapUserInfo user, IReadOnlyCollection<string> searchTokens)
    {
        foreach (var token in searchTokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (user.Username.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                user.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                user.Email.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                user.Dn.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string UnescapeLdapFilterValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return Regex.Replace(value, @"\\([0-9A-Fa-f]{2})", match =>
        {
            var hex = match.Groups[1].Value;
            var code = Convert.ToInt32(hex, 16);
            return char.ConvertFromUtf32(code).ToString();
        });
    }
}
