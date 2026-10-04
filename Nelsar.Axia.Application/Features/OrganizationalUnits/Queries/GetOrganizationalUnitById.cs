using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.OrganizationalUnits.Queries;

/// <summary>Null when no unit carries that id and the directory was read in full. Raises when
/// the enumeration was cut short, since then absence is unproven.</summary>
internal sealed record GetOrganizationalUnitByIdQuery(Guid Id) : IRequest<OrganizationalUnitDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.OrganizationalUnitsRead;
}

internal sealed class GetOrganizationalUnitByIdQueryHandler
    : IRequestHandler<GetOrganizationalUnitByIdQuery, OrganizationalUnitDto?>
{
    private readonly OrganizationalUnitDirectory _directory;

    public GetOrganizationalUnitByIdQueryHandler(OrganizationalUnitDirectory directory) => _directory = directory;

    public async Task<OrganizationalUnitDto?> Handle(
        GetOrganizationalUnitByIdQuery request,
        CancellationToken cancellationToken)
    {
        var unit = await _directory.ResolveAsync(request.Id);
        return unit == null ? null : OrganizationalUnitDirectory.Map(unit);
    }
}
