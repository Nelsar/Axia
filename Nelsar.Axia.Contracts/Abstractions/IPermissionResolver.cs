namespace Nelsar.Axia.Contracts.Abstractions;

/// <summary>
/// Works out what a signed-in principal is allowed to do, per request.
///
/// Deliberately not read from the token. Permissions baked into a JWT at sign-in stay valid for
/// the token's lifetime, so revoking a group's right to delete would not take effect until
/// everyone's token expired — which makes the administration screen a promise the system does
/// not keep. Resolving per request means a revocation lands within the cache TTL.
/// </summary>
public interface IPermissionResolver
{
    /// <summary>
    /// The permissions held by the signed-in principal whose entry is
    /// <paramref name="distinguishedName"/>, as the union across every role its directory groups
    /// map to.
    /// </summary>
    /// <exception cref="Nelsar.Axia.Contracts.Exceptions.DirectoryUnavailableException">
    /// Group membership could not be determined. Deliberately propagated rather than answered
    /// with an empty set: "we could not find out" must not be served as "holds nothing", which
    /// would silently strip an administrator mid-session.
    /// </exception>
    /// <exception cref="Nelsar.Axia.Contracts.Exceptions.NotAuthenticatedException">
    /// The entry is no longer there — deleted, renamed or moved since the session was opened. The
    /// session no longer names anybody, and signing in again is what finds the entry's new name.
    /// </exception>
    Task<IReadOnlySet<string>> ResolveAsync(string distinguishedName, Guid? providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The permissions any entry would hold if it signed in — the same answer as
    /// <see cref="ResolveAsync"/>, asked about somebody other than the caller. Null when there is
    /// no such entry, which for a target is "not found" and not a reason to end the caller's own
    /// session.
    /// </summary>
    /// <exception cref="Nelsar.Axia.Contracts.Exceptions.DirectoryUnavailableException">
    /// Group membership could not be determined.
    /// </exception>
    Task<IReadOnlySet<string>?> ResolveForEntryAsync(string distinguishedName, Guid? providerId, CancellationToken cancellationToken = default);
}
