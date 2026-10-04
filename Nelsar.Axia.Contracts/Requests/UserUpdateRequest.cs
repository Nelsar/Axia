using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class UserUpdateRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    public string? Password { get; set; }
}
