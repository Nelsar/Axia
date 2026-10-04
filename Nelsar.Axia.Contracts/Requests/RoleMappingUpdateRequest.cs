using System;
using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class RoleMappingUpdateRequest
{
    public Guid? ProviderId { get; set; }

    [Required]
    public string GroupName { get; set; } = string.Empty;

    [Required]
    public string RoleName { get; set; } = string.Empty;

    public int Priority { get; set; }
}
