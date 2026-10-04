using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class ProviderUpdateRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Host { get; set; } = string.Empty;

    [Required]
    public int Port { get; set; }

    [Required]
    public string ProviderType { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Comma-separated additional hosts tried in order if Host is unreachable (failover).
    /// </summary>
    public string? FailoverHosts { get; set; }

    public string BaseDn { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;

    /// <summary>
    /// Plaintext bind password. Leave null/empty to keep the existing stored credential unchanged.
    /// </summary>
    public string? AdminPassword { get; set; }

    public string UserSearchFilter { get; set; } = "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))";
    public int UserSearchPageSize { get; set; } = 500;
}
