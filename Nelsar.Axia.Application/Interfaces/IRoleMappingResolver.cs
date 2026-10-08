using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Interfaces;

/// <summary>
/// Turns a user's raw LDAP group DNs into application roles, using the administrator-editable
/// mappings. Lives in the application layer because permission resolution needs it; the
/// implementation touches nothing but the role-mapping repository.
/// </summary>
public interface IRoleMappingResolver
{
    /// <summary>
    /// Every role the given groups grant, unioned. Two mapped groups can only ever add roles —
    /// there is no precedence and no way for one membership to take a role away, which keeps
    /// "why does this person have access" answerable by reading the list.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveRolesAsync(IEnumerable<string> groupDns, Guid? providerId);

    /// <summary>
    /// Only the roles the groups are actually mapped to — empty when none is, rather than the
    /// User role <see cref="ResolveRolesAsync"/> falls back to. "What does membership of this
    /// group confer?" is a question about the group, and the fallback is a fact about people.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveGrantedRolesAsync(IEnumerable<string> groupDns, Guid? providerId);

    /// <summary>
    /// Roles granted by any mapping whose group goes by <paramref name="groupName"/>, wherever it
    /// sits. For a group that does not exist yet, or is about to be renamed, and so has no DN to
    /// match exactly: a group created under a mapped group's name is either about to be granted
    /// its role or about to be mistaken for it, and neither should be decided by whoever is
    /// creating it.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveRolesForGroupNameAsync(string groupName, Guid? providerId);

    /// <summary>
    /// The single role reported as <c>UserDto.Role</c>. Kept because that field is part of the
    /// login response and the UI reads it; authorisation itself no longer uses it, and this is
    /// the one place the old winner-takes-all precedence still applies.
    /// </summary>
    Task<string> ResolvePrimaryRoleAsync(IEnumerable<string> groupDns, Guid? providerId);

    /// <summary>
    /// The mappings that can grant a role in this directory — its own and the global ones —
    /// leaving out rows written by name where those do not count. For resolving many accounts at
    /// once with <c>RoleMappingResolver.PrimaryRole</c>, instead of one query per account.
    /// </summary>
    Task<IReadOnlyList<RoleMapping>> GetEffectiveMappingsAsync(Guid? providerId);
}
