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
    /// The permissions held by <paramref name="subject"/>, as the union across every role their
    /// directory groups map to.
    /// </summary>
    /// <exception cref="Nelsar.Axia.Contracts.Exceptions.DirectoryUnavailableException">
    /// Group membership could not be determined. Deliberately propagated rather than answered
    /// with an empty set: "we could not find out" must not be served as "holds nothing", which
    /// would silently strip an administrator mid-session.
    /// </exception>
    Task<IReadOnlySet<string>> ResolveAsync(string subject, Guid? providerId, CancellationToken cancellationToken = default);
}
