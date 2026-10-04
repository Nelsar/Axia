using System;

namespace Nelsar.Axia.Contracts.Common;

public record RoleMappingDto
(
    Guid Id,
    Guid? ProviderId,
    string GroupName,
    string RoleName,
    int Priority
);
