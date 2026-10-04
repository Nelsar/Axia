using Nelsar.Axia.Domain.Common;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Maps an LDAP group (by CN) to an application role. A null ProviderId applies globally, across
/// all LDAP domains; a set ProviderId scopes the mapping to one domain, letting different domains
/// use different group names for the same role.
///
/// This is an access-control rule, which is why it is worth the ceremony of private setters: a
/// mapping with a blank role name grants that group nothing, and one with a blank group name
/// matches nothing — both fail silently at sign-in, as a user who is simply missing permissions
/// they should have. The rules are enforced where the mapping is built so that never reaches the
/// table.
/// </summary>
public class RoleMapping
{
    public const int MaximumGroupNameLength = 256;
    public const int MaximumRoleNameLength = 128;

    /// <summary>For EF materialisation only.</summary>
    private RoleMapping()
    {
    }

    public Guid Id { get; private set; }
    public Guid? ProviderId { get; private set; }
    public string GroupName { get; private set; } = string.Empty;
    public string RoleName { get; private set; } = string.Empty;

    /// <summary>
    /// Lower value wins when a user belongs to multiple mapped groups.
    /// </summary>
    public int Priority { get; private set; }

    /// <exception cref="DomainValidationException">Any field breaks its rule.</exception>
    public static RoleMapping Create(Guid? providerId, string? groupName, string? roleName, int priority)
    {
        var mapping = new RoleMapping { Id = Guid.NewGuid() };

        mapping.ScopeTo(providerId);
        mapping.MapGroup(groupName);
        mapping.GrantRole(roleName);
        mapping.Reprioritise(priority);

        return mapping;
    }

    /// <summary>Null restores the mapping to every directory; a value confines it to one.</summary>
    public void ScopeTo(Guid? providerId) => ProviderId = providerId;

    public void MapGroup(string? groupName) =>
        GroupName = DomainGuard.RequiredText(groupName, nameof(GroupName), MaximumGroupNameLength);

    public void GrantRole(string? roleName) =>
        RoleName = DomainGuard.RequiredText(roleName, nameof(RoleName), MaximumRoleNameLength);

    /// <exception cref="DomainValidationException">
    /// The priority is negative. Ordering is by ascending priority, so a negative value is not
    /// merely unusual — it outranks the global default of 0 in a way nobody configuring it
    /// intends, which is how a low-privilege mapping quietly wins over an administrator one.
    /// </exception>
    public void Reprioritise(int priority)
    {
        if (priority < 0)
            throw new DomainValidationException(DomainMessages.PriorityNegative(priority));

        Priority = priority;
    }
}
