using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.OrganizationalUnits;

/// <summary>
/// Turning an organizational unit's API id back into a directory entry, and back into a DTO.
/// The id hashes the DN (see DeterministicIdFactory.BuildDirectoryObjectId), so it is stable
/// without anything being stored — and it changes when the entry is renamed or moved, because
/// the DN is what the directory considers the unit to be.
/// </summary>
internal sealed class OrganizationalUnitDirectory
{
    private readonly ILdapService _ldapService;

    public OrganizationalUnitDirectory(ILdapService ldapService)
    {
        _ldapService = ldapService;
    }

    public async Task<(IReadOnlyList<LdapOrganizationalUnit> Units, bool IsComplete)> ListAsync()
    {
        var listing = await _ldapService.GetOrganizationalUnitsAsync();
        return (listing?.Units?.ToList() ?? [], listing?.IsComplete ?? true);
    }

    /// <summary>
    /// Finds the unit an API id refers to. The id is a hash of the DN, so the only way back is
    /// to enumerate and hash — which also means a miss has two possible causes, and they must
    /// not be conflated: if the enumeration was truncated, the unit may exist in a page we
    /// never read, and answering 404 would tell the caller it is gone.
    /// </summary>
    public async Task<LdapOrganizationalUnit?> ResolveAsync(Guid id)
    {
        var (units, isComplete) = await ListAsync();

        var match = units.FirstOrDefault(unit => DeterministicIdFactory.BuildDirectoryObjectId(unit.Dn) == id);
        if (match != null)
            return match;

        if (!isComplete)
        {
            throw new DirectoryUnavailableException(
                ApplicationMessages.OrganizationalUnitLookupIncomplete(id.ToString()));
        }

        return null;
    }

    public static bool MatchesFilter(LdapOrganizationalUnit unit, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        // Filtered in memory rather than as an LDAP filter: the enumeration is already needed to
        // resolve ids, and matching here keeps "what the operator typed" out of a filter string.
        var terms = filter.Trim().Split(
            [' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

        return terms.All(term =>
            unit.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            unit.Dn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            unit.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static OrganizationalUnitDto Map(LdapOrganizationalUnit unit) => new()
    {
        Id = DeterministicIdFactory.BuildDirectoryObjectId(unit.Dn),
        Name = unit.Name,
        DistinguishedName = unit.Dn,
        ParentDistinguishedName = unit.ParentDn,
        Description = unit.Description
    };
}
