namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// Names the directory for work that has no HTTP request behind it.
///
/// <see cref="DefaultLdapProviderFactory.Resolve(Microsoft.AspNetCore.Http.HttpContext?)"/> answers
/// "which directory is this for?" by reading the header the request carried. That works for
/// everything dispatched from a controller and for nothing else: a background worker runs in its
/// own scope with no <c>HttpContext</c> at all, so the question had exactly one possible answer
/// there — the configured default — no matter which domain the work was actually meant for.
///
/// The consequence was the same one the fallback in that factory was removed to prevent. A bulk
/// batch queued against the branch domain was dequeued with no request behind it, resolved to the
/// default domain, and created every account in the wrong directory; the job then reported
/// success, because from the worker's point of view nothing had failed. This is that answer's
/// second source: whoever dispatches work off the request thread states the directory here, and
/// the factory reads it when there is no request to read instead.
///
/// Scoped, so the value belongs to one unit of work. Nothing sets it during an HTTP request — the
/// header remains the only source there — and a scope that leaves it unset resolves to the
/// default exactly as before.
/// </summary>
internal sealed class LdapProviderScope
{
    /// <summary>The directory this unit of work is for, or null for the configured default.</summary>
    public Guid? ProviderId { get; private set; }

    /// <summary>
    /// Called once, by whoever dispatches the work, before anything in the scope touches a
    /// directory. Null is meaningful and means the default: work queued by a caller who named no
    /// domain must keep running where it always did.
    /// </summary>
    public void Use(Guid? providerId) => ProviderId = providerId;
}
