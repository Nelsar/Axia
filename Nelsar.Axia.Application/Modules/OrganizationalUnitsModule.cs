using MediatR;
using Nelsar.Axia.Application.Features.OrganizationalUnits.Commands;
using Nelsar.Axia.Application.Features.OrganizationalUnits.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Modules;

/// <summary>The organizational units module. Same partial-update contract as
/// <see cref="GroupsModule"/>: optional fields travel through untouched.</summary>
internal sealed class OrganizationalUnitsModule : IOrganizationalUnitsModule
{
    private readonly ISender _sender;

    public OrganizationalUnitsModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<PagedResponse<OrganizationalUnitDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new SearchOrganizationalUnitsQuery(filter, pageSize, pageToken), cancellationToken);

    public Task<OrganizationalUnitDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetOrganizationalUnitByIdQuery(id), cancellationToken);

    public Task<OrganizationalUnitDto> CreateAsync(
        CreateOrganizationalUnitRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new CreateOrganizationalUnitCommand(
                request.Name,
                request.ParentDistinguishedName,
                request.Description),
            cancellationToken);

    public Task<OrganizationalUnitDto?> UpdateAsync(
        Guid id,
        UpdateOrganizationalUnitRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new UpdateOrganizationalUnitCommand(
                id,
                request.Name,
                request.ParentDistinguishedName,
                request.Description),
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeleteOrganizationalUnitCommand(id), cancellationToken);
}
