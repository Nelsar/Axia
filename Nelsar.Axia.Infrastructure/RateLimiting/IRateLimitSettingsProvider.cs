namespace Nelsar.Axia.Infrastructure.RateLimiting;

public record RateLimitPolicySettings(int PermitLimit, int WindowSeconds, int QueueLimit, bool Enabled);

/// <summary>
/// Fast, synchronous, short-lived-cache read path for the rate limiter's per-request
/// partitioner callback (which ASP.NET Core requires to be synchronous). Backed by
/// <see cref="Nelsar.Axia.Domain.Repositories.IRateLimitSettingsRepository"/> so an admin can
/// tune limits via the API without a redeploy; changes take effect within the cache TTL.
/// Public: consumed directly by Program.cs's AddRateLimiter policy factory.
/// </summary>
public interface IRateLimitSettingsProvider
{
    RateLimitPolicySettings GetSettings(string policyName);
}
