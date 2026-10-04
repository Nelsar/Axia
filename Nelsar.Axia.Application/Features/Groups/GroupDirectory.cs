using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.Groups;

/// <summary>
/// Turning a group's API id back into a directory entry, and back into a DTO.
///
/// Not a use case, for the same reason <see cref="Users.UserDirectory"/> is not: the id is a
/// hash of the DN, so resolving it means enumerating and hashing, and a miss has two causes that
/// must not be conflated. All five group handlers need that, and five copies would drift.
/// </summary>
internal sealed class GroupDirectory
{
    private readonly ILdapService _ldapService;

    public GroupDirectory(ILdapService ldapService)
    {
        _ldapService = ldapService;
    }

    public async Task<(IReadOnlyList<LdapGroup> Groups, bool IsComplete)> ListAsync()
    {
        var listing = await _ldapService.GetGroupsAsync();
        return (listing?.Groups?.ToList() ?? [], listing?.IsComplete ?? true);
    }

    /// <summary>
    /// Finds the group an API id refers to. Same shape as the organizational unit and computer
    /// resolvers and for the same reason: the id is a hash of the DN, so the only way back is to
    /// enumerate and hash — and a miss against a truncated enumeration is not evidence of absence.
    /// </summary>
    public async Task<LdapGroup?> ResolveAsync(Guid id)
    {
        var (groups, isComplete) = await ListAsync();

        var match = groups.FirstOrDefault(group => DeterministicIdFactory.BuildDirectoryObjectId(group.Dn) == id);
        if (match != null)
            return match;

        if (!isComplete)
            throw new DirectoryUnavailableException(ApplicationMessages.GroupLookupIncomplete(id.ToString()));

        return null;
    }

    public static bool MatchesFilter(LdapGroup group, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        // Name, DN and description only. Member DNs are deliberately not matched: a term that hit
        // a member would return groups whose own name and description have nothing to do with it,
        // which is a different question ("which groups contain X") wearing this one's clothes.
        var terms = filter.Trim().Split(
            [' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

        return terms.All(term =>
            group.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            group.Dn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            group.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static GroupDto Map(LdapGroup group) => new()
    {
        Id = DeterministicIdFactory.BuildDirectoryObjectId(group.Dn),
        Name = group.Name,
        DistinguishedName = group.Dn,
        ParentDistinguishedName = group.ParentDn,
        Description = group.Description,
        Members = group.Members
    };

    /// <summary>
    /// Trims, drops blanks and de-duplicates member DNs. A repeated member is a directory error
    /// (attributeOrValueExists) rather than an extra member, and a blank one is nothing at all.
    /// </summary>
    public static IReadOnlyList<string> NormaliseMembers(IEnumerable<string>? members) =>
        (members ?? Array.Empty<string>())
            .Select(member => (member ?? string.Empty).Trim())
            .Where(member => member.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
