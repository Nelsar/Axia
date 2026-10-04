using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Services;

internal class RoleMappingResolver : IRoleMappingResolver
{
    private readonly IRoleMappingRepository _repo;

    public RoleMappingResolver(IRoleMappingRepository repo)
    {
        _repo = repo;
    }

    public async Task<IReadOnlySet<string>> ResolveRolesAsync(IEnumerable<string> groupDns, Guid? providerId)
    {
        var cns = ExtractGroupNames(groupDns);
        if (cns.Count == 0)
            return Single(UserRoles.User);

        var matched = (await MatchingMappingsAsync(cns, providerId))
            .Select(m => m.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Someone in no mapped group is still a user; they simply hold whatever the User role
        // has been granted. Returning an empty set here would lock them out entirely.
        return matched.Count == 0 ? Single(UserRoles.User) : matched;
    }

    public async Task<string> ResolvePrimaryRoleAsync(IEnumerable<string> groupDns, Guid? providerId)
    {
        var cns = ExtractGroupNames(groupDns);
        if (cns.Count == 0)
            return UserRoles.User;

        var best = (await MatchingMappingsAsync(cns, providerId))
            // A mapping scoped to this provider takes precedence over a global one
            // for the same group name, regardless of Priority.
            .OrderByDescending(m => m.ProviderId != null)
            .ThenBy(m => m.Priority)
            .FirstOrDefault();

        return best?.RoleName ?? UserRoles.User;
    }

    private async Task<IReadOnlyList<Domain.Entities.RoleMapping>> MatchingMappingsAsync(
        IReadOnlyCollection<string> groupNames,
        Guid? providerId)
    {
        var mappings = await _repo.GetActiveForProviderAsync(providerId);

        return mappings
            .Where(m => groupNames.Contains(m.GroupName, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> ExtractGroupNames(IEnumerable<string> groupDns) =>
        groupDns
            .Select(dn => DistinguishedName.TryCreate(dn, out var parsed) ? parsed!.Cn : string.Empty)
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

    private static IReadOnlySet<string> Single(string role) =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role };
}
