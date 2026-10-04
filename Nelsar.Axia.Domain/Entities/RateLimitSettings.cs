using Nelsar.Axia.Domain.Common;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Configurable fixed-window rate limit for a named ASP.NET Core rate limiting policy
/// (e.g. "auth-login"). Rows are looked up by PolicyName at request time so limits can be tuned
/// without a redeploy.
///
/// The invariants are held here rather than only in the command validator because this row is
/// the brake on credential stuffing. A permit limit of zero wedges the policy shut for everyone
/// — a denial of service configured by hand — and a window of zero divides the limit by no time
/// at all. Both were reachable by any path that did not happen to go through the HTTP validator:
/// the seeder, a test, a future import.
/// </summary>
public class RateLimitSettings
{
    public const int MaximumPolicyNameLength = 128;
    public const int MinimumPermitLimit = 1;
    public const int MinimumWindowSeconds = 1;

    /// <summary>For EF materialisation only.</summary>
    private RateLimitSettings()
    {
    }

    public Guid Id { get; private set; }
    public string PolicyName { get; private set; } = string.Empty;
    public int PermitLimit { get; private set; } = 5;
    public int WindowSeconds { get; private set; } = 60;
    public int QueueLimit { get; private set; }
    public bool Enabled { get; private set; } = true;

    /// <exception cref="DomainValidationException">Any field breaks its rule.</exception>
    public static RateLimitSettings Create(
        string? policyName,
        int permitLimit,
        int windowSeconds,
        int queueLimit,
        bool enabled)
    {
        var settings = new RateLimitSettings
        {
            Id = Guid.NewGuid(),
            PolicyName = DomainGuard.RequiredText(policyName, nameof(PolicyName), MaximumPolicyNameLength)
        };

        settings.Adjust(permitLimit, windowSeconds, queueLimit);

        if (enabled)
            settings.Enable();
        else
            settings.Disable();

        return settings;
    }

    /// <summary>
    /// The three numbers change together: they describe one window, and applying a new permit
    /// limit against the old window is a limit nobody asked for.
    /// </summary>
    /// <exception cref="DomainValidationException">Any of the three is outside its range.</exception>
    public void Adjust(int permitLimit, int windowSeconds, int queueLimit)
    {
        if (permitLimit < MinimumPermitLimit)
            throw new DomainValidationException(DomainMessages.RateLimitPermitLimitInvalid(permitLimit));

        if (windowSeconds < MinimumWindowSeconds)
            throw new DomainValidationException(DomainMessages.RateLimitWindowInvalid(windowSeconds));

        if (queueLimit < 0)
            throw new DomainValidationException(DomainMessages.RateLimitQueueLimitNegative(queueLimit));

        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
        QueueLimit = queueLimit;
    }

    public void Enable() => Enabled = true;

    /// <summary>
    /// Turns the policy off entirely — the endpoint it guards then accepts unlimited attempts.
    /// Kept as its own method rather than a boolean argument so that switching off the brake on
    /// the login endpoint reads as what it is at the call site.
    /// </summary>
    public void Disable() => Enabled = false;
}
