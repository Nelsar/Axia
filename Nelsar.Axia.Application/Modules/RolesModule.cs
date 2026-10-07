using MediatR;
using Nelsar.Axia.Application.Features.Roles.Commands;
using Nelsar.Axia.Application.Features.Roles.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Application.Modules;

internal sealed class RolesModule : IRolesModule
{
    private readonly ISender _sender;

    public RolesModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<RolesOverviewDto> GetAllAsync(CancellationToken cancellationToken = default) =>
        _sender.Send(new GetRolesQuery(), cancellationToken);

    public Task<RolePermissionsDto> UpdatePermissionsAsync(
        string roleName,
        RolePermissionsUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new UpdateRolePermissionsCommand(roleName, request.Permissions ?? []), cancellationToken);
}
