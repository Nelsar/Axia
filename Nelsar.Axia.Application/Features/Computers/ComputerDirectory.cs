using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.Computers;

/// <summary>
/// Turning a computer account's API id back into a directory entry, and back into a DTO.
///
/// On the same terms as groups and organizational units: addressed by an id that hashes the DN,
/// filtered in memory, paged over the enumeration. What differs is what a write means — see
/// CreateComputerRequest and UpdateComputerRequest, which spell out that this manages directory
/// entries, not a machine's membership of the domain.
/// </summary>
internal sealed class ComputerDirectory
{
    private readonly ILdapService _ldapService;

    public ComputerDirectory(ILdapService ldapService)
    {
        _ldapService = ldapService;
    }

    public async Task<(IReadOnlyList<LdapComputer> Computers, bool IsComplete)> ListAsync()
    {
        var listing = await _ldapService.GetComputersAsync();
        return (listing?.Computers?.ToList() ?? [], listing?.IsComplete ?? true);
    }

    /// <summary>
    /// Finds the computer an API id refers to. Same shape as the group resolver and for the same
    /// reason: the id is a hash of the DN, so the only way back is to enumerate and hash — and a
    /// miss against a truncated enumeration is not evidence of absence.
    /// </summary>
    public async Task<LdapComputer?> ResolveAsync(Guid id)
    {
        var (computers, isComplete) = await ListAsync();

        var match = computers.FirstOrDefault(
            computer => DeterministicIdFactory.BuildDirectoryObjectId(computer.Dn) == id);
        if (match != null)
            return match;

        if (!isComplete)
            throw new DirectoryUnavailableException(ApplicationMessages.ComputerLookupIncomplete(id.ToString()));

        return null;
    }

    public static bool MatchesFilter(LdapComputer computer, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var terms = filter.Trim().Split(
            [' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

        // DNS name and operating system are matched alongside name, DN and description: they are
        // what an operator actually searches an inventory by ("all the 2012 servers"), and they
        // describe the machine itself rather than something it is merely related to.
        return terms.All(term =>
            computer.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            computer.Dn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            computer.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            computer.DnsHostName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            computer.OperatingSystem.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            computer.OperatingSystemVersion.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static ComputerDto Map(LdapComputer computer) => new()
    {
        Id = DeterministicIdFactory.BuildDirectoryObjectId(computer.Dn),
        Name = computer.Name,
        DistinguishedName = computer.Dn,
        ParentDistinguishedName = computer.ParentDn,
        Description = computer.Description,
        DnsHostName = computer.DnsHostName,
        OperatingSystem = computer.OperatingSystem,
        OperatingSystemVersion = computer.OperatingSystemVersion,
        Enabled = computer.Enabled
    };
}
