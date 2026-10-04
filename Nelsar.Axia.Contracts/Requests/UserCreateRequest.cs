using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class UserCreateRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
