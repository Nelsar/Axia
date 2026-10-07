using MediatR;
using Nelsar.Axia.Application.Features.Roles.Queries;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Modules;

/// <summary>
/// Registration and sign-in. Registration is the same command <see cref="UsersModule.CreateAsync"/>
/// sends — the difference is who is allowed to reach it, which the host decides, not this class.
/// </summary>
internal sealed class AuthModule : IAuthModule
{
    private readonly ISender _sender;

    public AuthModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<UserDto> RegisterAsync(UserCreateRequest request, CancellationToken cancellationToken = default) =>
        _sender.Send(new CreateUserCommand(request.Email, request.Name, request.Password), cancellationToken);

    public Task<AuthenticateResponse?> LoginAsync(
        AuthenticateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new AuthenticateCommand(request.Email, request.Password), cancellationToken);

    public Task<IReadOnlyList<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default) =>
        _sender.Send(new GetMyPermissionsQuery(), cancellationToken);
}
