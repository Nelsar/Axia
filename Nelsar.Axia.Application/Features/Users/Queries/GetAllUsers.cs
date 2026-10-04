using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Queries;

internal sealed record GetAllUsersQuery : IRequest<UserListingResponse>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersList;
}

internal sealed class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, UserListingResponse>
{
    private readonly UserDirectory _directory;

    public GetAllUsersQueryHandler(UserDirectory directory)
    {
        _directory = directory;
    }

    public Task<UserListingResponse> Handle(GetAllUsersQuery request, CancellationToken cancellationToken) =>
        _directory.ListAsync();
}
