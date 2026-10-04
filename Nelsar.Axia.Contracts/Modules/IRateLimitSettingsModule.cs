using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>Per-policy rate limits, retunable at run time.</summary>
public interface IRateLimitSettingsModule : IOpencoreModule
{
    Task<IEnumerable<RateLimitSettingsDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<RateLimitSettingsDto?> GetByPolicyNameAsync(
        string policyName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the settings for a named policy, e.g. "auth-login". Takes effect within
    /// the provider's cache TTL — no restart needed.
    /// </summary>
    Task<RateLimitSettingsDto> UpsertAsync(
        string policyName,
        RateLimitSettingsUpdateRequest request,
        CancellationToken cancellationToken = default);
}
