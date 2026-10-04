using System;
using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class RoleMappingCreateRequest
{
    /// <summary>
    /// Scope this mapping to one LDAP domain (Provider.Id). Leave null for a global mapping.
    /// </summary>
    public Guid? ProviderId { get; set; }

    [Required]
    public string GroupName { get; set; } = string.Empty;

    [Required]
    public string RoleName { get; set; } = string.Empty;

    public int Priority { get; set; }
}
