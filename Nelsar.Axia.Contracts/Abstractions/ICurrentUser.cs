namespace Nelsar.Axia.Contracts.Abstractions;

/// <summary>
/// Who is asking, as far as the system is concerned.
///
/// The use cases authorise themselves (see <c>IRequirePermission</c>) and cannot reach for
/// HttpContext to find out who is calling — that is a hosting concern, and depending on it would
/// put ASP.NET Core underneath the business rules. So the host answers the question instead: this
/// is the port, the HTTP API supplies the adapter, and a background service or a test supplies
/// its own.
///
/// It lives here rather than in Application for the same reason <see cref="Modules.IOpencoreModule"/>
/// does. Both are the boundary — one is what a host may ask of the system, this is what the system
/// asks back — and a host that had to reference Application to implement one of them would be
/// referencing Application for the sake of a single interface.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The login permissions are resolved by — the same value the directory is searched with.
    /// Null when nobody is signed in.
    /// </summary>
    string? Subject { get; }

    /// <summary>
    /// Which directory the caller authenticated against, when they named one. Permissions are
    /// resolved per provider because the same group name can mean different things in different
    /// domains.
    /// </summary>
    Guid? ProviderId { get; }

    /// <summary>
    /// Work the application runs on its own behalf — a queued job, a seeder — rather than for a
    /// signed-in caller. Such work carries no principal to resolve permissions for; it was
    /// authorised when it was requested, which is the only point at which a person was present.
    /// </summary>
    bool IsSystem { get; }
}
