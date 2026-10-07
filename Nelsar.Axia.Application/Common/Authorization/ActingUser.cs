using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Application.Common.Authorization;

/// <summary>
/// The principal a use case runs on behalf of, whichever way it was dispatched.
///
/// During an HTTP request that is the host's <see cref="ICurrentUser"/>. In queued work it is
/// the person who queued it, as stated through <see cref="BackgroundPrincipal"/> — the host's
/// principal is empty there, because there is no request for it to read. Authorisation and the
/// audit trail both ask this rather than <see cref="ICurrentUser"/> directly, so they agree on
/// who the caller is and neither treats queued work as anonymous.
/// </summary>
internal sealed class ActingUser
{
    private readonly ICurrentUser _currentUser;
    private readonly BackgroundPrincipal _background;

    public ActingUser(ICurrentUser currentUser, BackgroundPrincipal background)
    {
        _currentUser = currentUser;
        _background = background;
    }

    public string? Subject => _background.IsSet ? _background.Subject : _currentUser.Subject;

    public string? DistinguishedName =>
        _background.IsSet ? _background.DistinguishedName : _currentUser.DistinguishedName;

    public Guid? ProviderId => _background.IsSet ? _background.ProviderId : _currentUser.ProviderId;

    /// <summary>
    /// Work that runs as a person is never system work, whatever the host reports: it is checked
    /// as that person.
    /// </summary>
    public bool IsSystem => !_background.IsSet && _currentUser.IsSystem;
}
