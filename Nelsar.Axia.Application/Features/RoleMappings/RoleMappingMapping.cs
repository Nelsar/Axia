using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.RoleMappings;

internal static class RoleMappingMapping
{
    public static RoleMappingDto ToDto(RoleMapping m) =>
        new(m.Id, m.ProviderId, m.GroupName, m.RoleName, m.Priority);
}
