using Microsoft.Extensions.Caching.Memory;
using Nelsar.Axia.Infrastructure.RateLimiting;

namespace Nelsar.Axia.Api.Authorization;

/// <summary>
/// Failed sign-ins counted per account rather than per client address.
///
/// The auth-login policy limits each address, which does nothing against guesses spread over many
/// addresses, all aimed at one account — and every guess the API passes on is a bind against that
/// account in the directory, counting towards whatever lockout the directory applies. So failures
/// are also counted per login, per directory, in a fixed window: once a login has failed
/// <see cref="RateLimitPolicySettings.PermitLimit"/> times in
/// <see cref="RateLimitPolicySettings.WindowSeconds"/>, its further attempts are refused without
/// asking the directory until the window ends. A successful sign-in clears the count.
///
/// The limits are the <see cref="PolicyName"/> row on the rate limiting page, and disabling it
/// switches this off. Set below the directory's own lockout threshold, it also keeps attempts made
/// through the application from locking the account there. The refusal is the same for logins
/// that exist and logins that do not, so it tells nobody which is which.
/// </summary>
public sealed class SignInThrottle
{
    public const string PolicyName = "auth-login-account";

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;

    public SignInThrottle(IMemoryCache cache, TimeProvider time)
    {
        _cache = cache;
        _time = time;
    }

    private sealed class Failures
    {
        public required DateTimeOffset WindowEnds { get; init; }
        public int Count;
    }

    /// <summary>How long until <paramref name="login"/> may try again; null when it may now.</summary>
    public TimeSpan? Blocked(string login, string? directory, RateLimitPolicySettings settings)
    {
        if (!settings.Enabled || !_cache.TryGetValue(Key(login, directory), out Failures? failures) || failures is null)
            return null;

        var remaining = failures.WindowEnds - _time.GetUtcNow();
        lock (failures)
            return failures.Count >= settings.PermitLimit && remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RecordFailure(string login, string? directory, RateLimitPolicySettings settings)
    {
        if (!settings.Enabled)
            return;

        var now = _time.GetUtcNow();
        var key = Key(login, directory);

        if (!_cache.TryGetValue(key, out Failures? failures) || failures is null || failures.WindowEnds <= now)
        {
            var window = TimeSpan.FromSeconds(settings.WindowSeconds);
            failures = new Failures { WindowEnds = now + window };

            // Relative, so the entry's lifetime does not depend on the cache's clock agreeing with
            // this one; WindowEnds is what decides, and the entry only has to outlive it.
            _cache.Set(key, failures, window);
        }

        lock (failures)
            failures.Count++;
    }

    public void Reset(string login, string? directory) => _cache.Remove(Key(login, directory));

    /// <summary>
    /// The login as the directory compares it — ignoring case and surrounding space — so that
    /// "Admin" and "admin " are one account here as they are there. Per directory, because the
    /// same login in two directories is two accounts.
    /// </summary>
    private static string Key(string login, string? directory) =>
        $"signin-failures:{directory?.Trim().ToLowerInvariant()}:{login.Trim().ToLowerInvariant()}";
}
