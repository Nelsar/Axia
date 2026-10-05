namespace Nelsar.Axia.Application.Services;

/// <summary>
/// Who a unit of queued work runs as, once it is running with no request behind it.
///
/// A queued bulk job used to run as nobody. The worker has no <c>HttpContext</c>, so there was
/// no principal to check permissions against and no actor to write into the audit trail, and the
/// command it dispatched had to carry no requirement at all for the job to run. The batch was
/// checked once, when it was queued, against <c>users.bulk</c> alone — which said nothing about
/// what its rows did.
///
/// Now the person who queued the job is recorded on it, and the worker states that here before
/// dispatching anything. The job then runs as them: its rows are authorised against what they
/// hold at the time it runs, so a permission withdrawn while the job waited is withdrawn from the
/// job too, and every change it makes is audited under their name.
///
/// Scoped, so the value belongs to one unit of work. Nothing sets it during an HTTP request,
/// where the request's own principal is the only source.
/// </summary>
public sealed class BackgroundPrincipal
{
    /// <summary>The subject the work runs as — the login permissions are resolved for.</summary>
    public string? Subject { get; private set; }

    /// <summary>The directory the work is for, or null for the configured default.</summary>
    public Guid? ProviderId { get; private set; }

    /// <summary>Whether <see cref="RunAs"/> has been called in this scope.</summary>
    public bool IsSet { get; private set; }

    /// <summary>
    /// Called once, by whoever dispatches the work, before anything in the scope runs. A null
    /// subject is allowed and means the work was queued by nobody; guarded use cases then refuse
    /// it, which is the answer such a job should get.
    /// </summary>
    public void RunAs(string? subject, Guid? providerId)
    {
        Subject = subject;
        ProviderId = providerId;
        IsSet = true;
    }
}
