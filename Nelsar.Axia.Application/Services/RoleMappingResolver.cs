using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Services;

internal class RoleMappingResolver : IRoleMappingResolver
{
    private readonly IRoleMappingRepository _repo;
    private readonly bool _allowMappingsByName;

    public RoleMappingResolver(IRoleMappingRepository repo, IOptions<RoleMappingOptions> options)
    {
        _repo = repo;
        _allowMappingsByName = options.Value.AllowMappingsByName;
    }

    public async Task<IReadOnlySet<string>> ResolveRolesAsync(IEnumerable<string> groupDns, Guid? providerId)
    {
        var matched = await ResolveGrantedRolesAsync(groupDns, providerId);

        // Someone in no mapped group is still a user; they simply hold whatever the User role
        // has been granted. Returning an empty set here would lock them out entirely.
        return matched.Count == 0 ? Single(UserRoles.User) : matched;
    }

    public async Task<IReadOnlySet<string>> ResolveGrantedRolesAsync(IEnumerable<string> groupDns, Guid? providerId)
    {
        var groups = ParseGroups(groupDns);
        if (groups.Count == 0)
            return Single();

        return (await MatchingMappingsAsync(groups, providerId))
            .Select(m => m.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlySet<string>> ResolveRolesForGroupNameAsync(string groupName, Guid? providerId)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return Single();

        var mappings = await _repo.GetActiveForProviderAsync(providerId);

        return mappings
            .Where(m => m.NamesGroup(groupName))
            .Select(m => m.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> ResolvePrimaryRoleAsync(IEnumerable<string> groupDns, Guid? providerId)
    {
        var groups = ParseGroups(groupDns);
        if (groups.Count == 0)
            return UserRoles.User;

        var best = (await MatchingMappingsAsync(groups, providerId))
            // A mapping scoped to this provider takes precedence over a global one
            // for the same group name, regardless of Priority.
            .OrderByDescending(m => m.ProviderId != null)
            .ThenBy(m => m.Priority)
            .FirstOrDefault();

        return best?.RoleName ?? UserRoles.User;
    }

    /// <summary>
    /// Which mappings the groups satisfy. Whether a mapping matches a group is the mapping's own
    /// rule — the exact entry its DN names, or any group with its CN for a row still written by
    /// name — so it is asked rather than restated here. This used to compare CNs for every row,
    /// which is what let any group called "Administrators" grant the Admin role.
    ///
    /// Rows written by name take part only where RoleMappings:AllowMappingsByName says so; by
    /// default they grant nothing, and DbInitializer says at startup which ones are being ignored.
    /// </summary>
    private async Task<IReadOnlyList<Domain.Entities.RoleMapping>> MatchingMappingsAsync(
        IReadOnlyCollection<DistinguishedName> groups,
        Guid? providerId)
    {
        var mappings = await _repo.GetActiveForProviderAsync(providerId);

        return mappings
            .Where(m => _allowMappingsByName || !m.IdentifiesGroupByNameOnly)
            .Where(m => groups.Any(m.Matches))
            .ToList();
    }

    private static List<DistinguishedName> ParseGroups(IEnumerable<string> groupDns) =>
        groupDns
            .Select(dn => DistinguishedName.TryCreate(dn, out var parsed) ? parsed : null)
            .OfType<DistinguishedName>()
            .ToList();

    private static IReadOnlySet<string> Single(params string[] roles) =>
        new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
}
