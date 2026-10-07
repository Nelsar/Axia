using Nelsar.Axia.Domain.Common;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Maps an LDAP group, by its distinguished name, to an application role. A null ProviderId
/// applies globally, across all LDAP domains; a set ProviderId scopes the mapping to one domain,
/// letting different domains use different groups for the same role.
///
/// This is an access-control rule, which is why it is worth the ceremony of private setters: a
/// mapping with a blank role name grants that group nothing, and one with a blank group name
/// matches nothing — both fail silently at sign-in, as a user who is simply missing permissions
/// they should have. The rules are enforced where the mapping is built so that never reaches the
/// table.
///
/// The group used to be named by its CN alone, and matched any group carrying that CN. A CN is
/// unique only within its container, so "Administrators" granted the Admin role to members of
/// every group called Administrators anywhere in the directory — including one created that
/// afternoon in an OU whose owner can create groups, with themselves as its only member. A DN
/// names exactly one entry, and is what a mapping is written with now.
///
/// Rows written by name before that remain, and keep matching the way they always did, because
/// refusing them outright would take the Admin role away from every administrator at the moment
/// of upgrade, with nobody left who could put it back. They are reported as
/// <see cref="IdentifiesGroupByNameOnly"/> so an administrator can see which ones still need a DN,
/// and editing one requires giving it one.
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

    /// <summary>
    /// Whether this row still names its group by CN alone, as every row did before mappings
    /// took a DN. Such a row matches any group with that CN, wherever it sits — see the type's
    /// remarks for why that is a hole, and why the row is kept regardless.
    /// </summary>
    public bool IdentifiesGroupByNameOnly => !DistinguishedName.IsWellFormed(GroupName);

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

    /// <summary>
    /// A mapping that names its group by CN only — the form <see cref="Create"/> no longer
    /// accepts. For the seeder alone: a fresh database has to grant the Admin role to somebody
    /// before anyone can sign in to configure anything, and the seeder cannot know where in a
    /// directory it has never seen the administrators' group lives. The row it writes is
    /// reported as <see cref="IdentifiesGroupByNameOnly"/> until an administrator gives it a DN.
    /// </summary>
    /// <exception cref="DomainValidationException">Any field other than the group breaks its rule.</exception>
    public static RoleMapping SeedByGroupName(Guid? providerId, string? groupName, string? roleName, int priority)
    {
        var mapping = new RoleMapping { Id = Guid.NewGuid() };

        mapping.ScopeTo(providerId);
        mapping.GroupName = DomainGuard.RequiredText(groupName, nameof(GroupName), MaximumGroupNameLength);
        mapping.GrantRole(roleName);
        mapping.Reprioritise(priority);

        return mapping;
    }

    /// <summary>Null restores the mapping to every directory; a value confines it to one.</summary>
    public void ScopeTo(Guid? providerId) => ProviderId = providerId;

    /// <exception cref="DomainValidationException">
    /// The group is not given as a distinguished name. A bare CN is refused rather than stored,
    /// for the reason in the type's remarks: it would match groups nobody meant to map.
    /// </exception>
    public void MapGroup(string? groupName)
    {
        var value = DomainGuard.RequiredText(groupName, nameof(GroupName), MaximumGroupNameLength);

        if (!DistinguishedName.IsWellFormed(value))
            throw new DomainValidationException(DomainMessages.GroupMappingNeedsDistinguishedName(value));

        GroupName = DistinguishedName.Create(value).Normalized;
    }

    /// <summary>
    /// Whether membership of <paramref name="group"/> grants this mapping's role: the very entry
    /// the mapping names, or — for a row still written by name — any group with that CN.
    /// </summary>
    public bool Matches(DistinguishedName group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return IdentifiesGroupByNameOnly
            ? string.Equals(group.Cn, GroupName.Trim(), StringComparison.OrdinalIgnoreCase)
            : string.Equals(group.Normalized, DistinguishedName.Create(GroupName).Normalized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a group called <paramref name="groupName"/> would share its name with the group
    /// this mapping grants a role to. Wider than <see cref="Matches"/> on purpose: it answers
    /// "could a group of this name be, or be mistaken for, a mapped one?" before the group exists
    /// and has a DN to compare.
    /// </summary>
    public bool NamesGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return false;

        var mappedName = IdentifiesGroupByNameOnly
            ? GroupName.Trim()
            : DistinguishedName.Create(GroupName).Rdn;

        return string.Equals(mappedName, groupName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

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
