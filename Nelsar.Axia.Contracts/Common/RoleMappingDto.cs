using System;

namespace Nelsar.Axia.Contracts.Common;

/// <param name="IdentifiesGroupByNameOnly">
/// The mapping still names its group by CN alone, from before mappings took a DN, and grants its
/// role to members of every group that shares that name. It keeps working so nobody loses access
/// on upgrade; it should be edited to the group's distinguished name.
/// </param>
public record RoleMappingDto
(
    Guid Id,
    Guid? ProviderId,
    string GroupName,
    string RoleName,
    int Priority,
    bool IdentifiesGroupByNameOnly = false
);
