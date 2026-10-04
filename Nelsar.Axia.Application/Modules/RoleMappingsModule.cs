using MediatR;
using Nelsar.Axia.Application.Features.RoleMappings.Commands;
using Nelsar.Axia.Application.Features.RoleMappings.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Application.Modules;

/// <summary>The role mappings module.</summary>
internal sealed class RoleMappingsModule : IRoleMappingsModule
{
    private readonly ISender _sender;

    public RoleMappingsModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<IEnumerable<RoleMappingDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _sender.Send(new GetAllRoleMappingsQuery(), cancellationToken);

    public Task<RoleMappingDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetRoleMappingByIdQuery(id), cancellationToken);

    public Task<RoleMappingDto> CreateAsync(
        RoleMappingCreateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new CreateRoleMappingCommand(
                request.ProviderId,
                request.GroupName,
                request.RoleName,
                request.Priority),
            cancellationToken);

    public Task<RoleMappingDto?> UpdateAsync(
        Guid id,
        RoleMappingUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new UpdateRoleMappingCommand(
                id,
                request.ProviderId,
                request.GroupName,
                request.RoleName,
                request.Priority),
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeleteRoleMappingCommand(id), cancellationToken);
}
