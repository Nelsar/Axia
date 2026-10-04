using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Domain.Repositories;

public interface IRateLimitSettingsRepository
{
    Task<IEnumerable<RateLimitSettings>> GetAllAsync();
    Task<RateLimitSettings?> GetByPolicyNameAsync(string policyName);
    Task<RateLimitSettings> UpsertAsync(RateLimitSettings settings);
}
