using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RoleMappings.Queries;

/// <summary>Null when there is no such mapping.</summary>
internal sealed record GetRoleMappingByIdQuery(Guid Id) : IRequest<RoleMappingDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RolesManage;
}

internal sealed class GetRoleMappingByIdQueryHandler : IRequestHandler<GetRoleMappingByIdQuery, RoleMappingDto?>
{
    private readonly IRoleMappingRepository _repo;

    public GetRoleMappingByIdQueryHandler(IRoleMappingRepository repo) => _repo = repo;

    public async Task<RoleMappingDto?> Handle(GetRoleMappingByIdQuery request, CancellationToken cancellationToken)
    {
        var mapping = await _repo.GetByIdAsync(request.Id);
        return mapping == null ? null : RoleMappingMapping.ToDto(mapping);
    }
}
