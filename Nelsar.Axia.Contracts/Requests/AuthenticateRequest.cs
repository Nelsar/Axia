using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class AuthenticateRequest
{
    /// <summary>
    /// The login as the directory knows it — an address (<c>user@example.org</c>), a bare
    /// sAMAccountName, or a uid. It is passed to the LDAP search as-is, which already tries
    /// mail, userPrincipalName, sAMAccountName and uid.
    ///
    /// Deliberately not <c>[EmailAddress]</c>: an Active Directory account is not required to
    /// carry mail or userPrincipalName at all, and validating the login as an address rejected
    /// those users with a 400 before the directory was ever asked. The property keeps its name
    /// so the JSON stays <c>{"email": ...}</c> for existing callers.
    /// </summary>
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
