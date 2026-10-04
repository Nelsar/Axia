using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>Null when there is no such account. A miss against a truncated directory walk is not
/// absence and raises instead — see <see cref="UserDirectory.ResolveAsync"/>.</summary>
internal sealed record GetUserByIdQuery(Guid Id) : IRequest<UserDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersRead;
}

internal sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly UserDirectory _directory;

    public GetUserByIdQueryHandler(UserDirectory directory)
    {
        _directory = directory;
    }

    public Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken) =>
        _directory.ResolveAsync(request.Id);
}
