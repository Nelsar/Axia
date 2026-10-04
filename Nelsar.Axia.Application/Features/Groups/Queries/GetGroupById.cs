using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Groups.Queries;

/// <summary>Null when no group carries that id and the directory was read in full. Raises when
/// the enumeration was cut short, since then absence is unproven.</summary>
internal sealed record GetGroupByIdQuery(Guid Id) : IRequest<GroupDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.GroupsRead;
}

internal sealed class GetGroupByIdQueryHandler : IRequestHandler<GetGroupByIdQuery, GroupDto?>
{
    private readonly GroupDirectory _directory;

    public GetGroupByIdQueryHandler(GroupDirectory directory) => _directory = directory;

    public async Task<GroupDto?> Handle(GetGroupByIdQuery request, CancellationToken cancellationToken)
    {
        var group = await _directory.ResolveAsync(request.Id);
        return group == null ? null : GroupDirectory.Map(group);
    }
}
