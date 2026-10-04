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
    /// The single role reported as <c>UserDto.Role</c>. Kept because that field is part of the
    /// login response and the UI reads it; authorisation itself no longer uses it, and this is
    /// the one place the old winner-takes-all precedence still applies.
    /// </summary>
    Task<string> ResolvePrimaryRoleAsync(IEnumerable<string> groupDns, Guid? providerId);
}
