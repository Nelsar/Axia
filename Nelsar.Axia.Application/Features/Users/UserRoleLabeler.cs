using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.Users;

/// <summary>
/// The role shown next to each account in a listing: the one signing in would give it.
///
/// Every listing used to say User for everybody, administrators included — the role was a
/// constant wherever a directory entry became a UserDto, and only sign-in ever worked it out.
///
/// Working it out per account the way sign-in does would be a directory read and a database query
/// for each of what can be thousands of entries. Instead the mappings are loaded once and each
/// group they name is read once for its members; an account's role then comes from its memberOf
/// together with those member lists, in memory. The member lists are what make it right where the
/// directory keeps no memberOf — OpenLDAP without the memberof overlay — as GroupsNaming does in
/// StandardLdapProvider for a single account.
///
/// It is a label. Authorisation never reads it: what an account may do is resolved per request
/// from its own entry, by PermissionResolver.
/// </summary>
internal sealed class UserRoleLabeler
{
    private readonly ILdapService _ldapService;
    private readonly IRoleMappingResolver _roleMappings;
    private readonly ActingUser _actingUser;
    private readonly ILogger<UserRoleLabeler> _logger;

    public UserRoleLabeler(
        ILdapService ldapService,
        IRoleMappingResolver roleMappings,
        ActingUser actingUser,
        ILogger<UserRoleLabeler> logger)
    {
        _ldapService = ldapService;
        _roleMappings = roleMappings;
        _actingUser = actingUser;
        _logger = logger;
    }

    /// <summary>The role for each entry, in the same order.</summary>
    public async Task<IReadOnlyList<string>> RolesForAsync(IReadOnlyList<LdapUserInfo> entries)
    {
        if (entries.Count == 0)
            return [];

        // The directory being listed is the session's, so its mappings are the ones that apply.
        var mappings = await _roleMappings.GetEffectiveMappingsAsync(_actingUser.ProviderId);

        // Only groups named by DN can be read. A row still written by name matches through
        // memberOf alone, as it does at sign-in.
        var mappedGroups = new List<(string Dn, DirectoryGroupMembers Members)>();
        foreach (var groupDn in mappings
                     .Where(m => !m.IdentifiesGroupByNameOnly)
                     .Select(m => m.GroupName)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            mappedGroups.Add((groupDn, await MembersOfAsync(groupDn)));
        }

        return entries
            .Select(entry => RoleMappingResolver.PrimaryRole(
                mappings,
                entry.Groups.Concat(mappedGroups.Where(group => group.Members.Includes(entry)).Select(group => group.Dn))))
            .ToList();
    }

    /// <summary>The role for one entry.</summary>
    public async Task<string> RoleForAsync(LdapUserInfo entry) => (await RolesForAsync([entry]))[0];

    private async Task<DirectoryGroupMembers> MembersOfAsync(string groupDn)
    {
        try
        {
            return await _ldapService.GetGroupMembersAsync(groupDn) ?? DirectoryGroupMembers.None;
        }
        catch (DirectoryUnavailableException ex)
        {
            // Not worth failing a listing that has already been read over a label. The accounts'
            // own memberOf still counts, so the labels are at worst what they were before.
            _logger.LogWarning(ex,
                "Could not read the members of mapped group {Group}; roles in this listing come from memberOf alone.",
                groupDn);
            return DirectoryGroupMembers.None;
        }
    }
}
