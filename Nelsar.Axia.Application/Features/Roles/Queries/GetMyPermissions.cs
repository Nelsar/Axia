using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Roles.Queries;

/// <summary>
/// What the caller may do, as the API will judge it — for the console to decide what to show.
///
/// The console used to decide from the single role in the sign-in response, which is the
/// lowest-priority role a person's groups map to, while the API grants the union of all of them.
/// Someone in both UserEditors and UserDeleters was allowed to delete and shown no delete button.
/// Asking the API is the only way the two cannot disagree.
///
/// Deliberately carries no <c>IRequirePermission</c>: any signed-in caller may ask what they
/// themselves hold.
/// </summary>
internal sealed record GetMyPermissionsQuery : IRequest<IReadOnlyList<string>>;

internal sealed class GetMyPermissionsQueryHandler : IRequestHandler<GetMyPermissionsQuery, IReadOnlyList<string>>
{
    private readonly ActingUser _actingUser;
    private readonly IPermissionResolver _permissions;

    public GetMyPermissionsQueryHandler(ActingUser actingUser, IPermissionResolver permissions)
    {
        _actingUser = actingUser;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<string>> Handle(GetMyPermissionsQuery request, CancellationToken cancellationToken)
    {
        var entry = _actingUser.DistinguishedName;
        if (string.IsNullOrWhiteSpace(entry))
            throw new NotAuthenticatedException(ApplicationMessages.AuthenticationRequired());

        var held = await _permissions.ResolveAsync(entry, _actingUser.ProviderId, cancellationToken);
        return Permissions.All.Where(held.Contains).ToList();
    }
}
