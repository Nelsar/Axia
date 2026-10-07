using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class ProviderCreateRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Host { get; set; } = string.Empty;

    [Required]
    public int Port { get; set; }

    /// <summary>
    /// How the connection is protected: <c>Ldaps</c>, <c>StartTls</c> or <c>None</c>. Omitted
    /// means <c>Ldaps</c>. <c>None</c> sends every bind, the bind password included, unencrypted.
    /// </summary>
    public string? Security { get; set; }

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
    /// Plaintext bind password, encrypted at rest by the service layer. Never returned by the API.
    /// </summary>
    public string? AdminPassword { get; set; }

    public string UserSearchFilter { get; set; } = "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))";
    public int UserSearchPageSize { get; set; } = 500;
}
