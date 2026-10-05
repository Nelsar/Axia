using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RoleMappings.Commands;

/// <summary>False when there is no such mapping.</summary>
internal sealed record DeleteRoleMappingCommand(Guid Id) : IRequest<bool>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RolesManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class DeleteRoleMappingCommandHandler : IRequestHandler<DeleteRoleMappingCommand, bool>
{
    private readonly IRoleMappingRepository _repo;

    public DeleteRoleMappingCommandHandler(IRoleMappingRepository repo) => _repo = repo;

    public Task<bool> Handle(DeleteRoleMappingCommand request, CancellationToken cancellationToken) =>
        _repo.DeleteAsync(request.Id);
}
