using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RoleMappings.Queries;

internal sealed record GetAllRoleMappingsQuery : IRequest<IEnumerable<RoleMappingDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RolesManage;
}

internal sealed class GetAllRoleMappingsQueryHandler
    : IRequestHandler<GetAllRoleMappingsQuery, IEnumerable<RoleMappingDto>>
{
    private readonly IRoleMappingRepository _repo;

    public GetAllRoleMappingsQueryHandler(IRoleMappingRepository repo) => _repo = repo;

    public async Task<IEnumerable<RoleMappingDto>> Handle(GetAllRoleMappingsQuery request, CancellationToken cancellationToken)
    {
        var list = await _repo.GetAllAsync();
        return list.Select(RoleMappingMapping.ToDto);
    }
}
