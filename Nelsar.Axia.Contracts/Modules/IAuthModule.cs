using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// Sign-up and sign-in. Kept apart from <see cref="IUsersModule"/> because these two are the
/// only operations a caller reaches before it has been authenticated at all, and a module is
/// the unit a host is granted access to.
/// </summary>
public interface IAuthModule : IOpencoreModule
{
    Task<UserDto> RegisterAsync(UserCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null means the credentials were refused — not that anything failed.</summary>
    Task<AuthenticateResponse?> LoginAsync(AuthenticateRequest request, CancellationToken cancellationToken = default);
}
