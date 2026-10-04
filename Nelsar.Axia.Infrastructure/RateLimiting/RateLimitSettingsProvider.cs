using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.RateLimiting;

internal class RateLimitSettingsProvider : IRateLimitSettingsProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly RateLimitPolicySettings Fallback = new(PermitLimit: 5, WindowSeconds: 60, QueueLimit: 0, Enabled: true);

    private readonly IRateLimitSettingsRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RateLimitSettingsProvider> _logger;

    public RateLimitSettingsProvider(IRateLimitSettingsRepository repo, IMemoryCache cache, ILogger<RateLimitSettingsProvider> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    public RateLimitPolicySettings GetSettings(string policyName)
    {
        var cacheKey = $"ratelimit:{policyName}";
        return _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            try
            {
                // The rate limiter partitioner callback is synchronous by design (ASP.NET
                // Core API shape); this blocking call only happens once per policy per
                // CacheTtl window, so the cost is bounded and predictable.
                var settings = _repo.GetByPolicyNameAsync(policyName).GetAwaiter().GetResult();
                if (settings == null)
                    return Fallback;

                return new RateLimitPolicySettings(settings.PermitLimit, settings.WindowSeconds, settings.QueueLimit, settings.Enabled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load rate limit settings for policy '{PolicyName}', using fallback.", policyName);
                return Fallback;
            }
        })!;
    }
}
