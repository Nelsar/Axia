using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RoleMappings.Commands;

/// <summary>Null when there is no such mapping.</summary>
internal sealed record UpdateRoleMappingCommand(
    Guid Id,
    Guid? ProviderId,
    string GroupName,
    string RoleName,
    int Priority) : IRequest<RoleMappingDto?>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RolesManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();

    /// <inheritdoc />
    public string? AuditDescription => $"group={GroupName}, role={RoleName}, provider={(ProviderId?.ToString() ?? "all")}, priority={Priority}";
}

internal sealed class UpdateRoleMappingCommandValidator : AbstractValidator<UpdateRoleMappingCommand>
{
    public UpdateRoleMappingCommandValidator()
    {
        RuleFor(c => c.GroupName).NotEmpty();
        RuleFor(c => c.RoleName).NotEmpty();
    }
}

internal sealed class UpdateRoleMappingCommandHandler : IRequestHandler<UpdateRoleMappingCommand, RoleMappingDto?>
{
    private readonly IRoleMappingRepository _repo;

    public UpdateRoleMappingCommandHandler(IRoleMappingRepository repo) => _repo = repo;

    public async Task<RoleMappingDto?> Handle(UpdateRoleMappingCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetByIdAsync(request.Id);
        if (existing == null)
            return null;

        // Applied to the mapping itself rather than to a detached stand-in the repository then
        // copies across — the same reason as UpdateProviderCommandHandler.
        existing.ScopeTo(request.ProviderId);
        existing.MapGroup(request.GroupName);
        existing.GrantRole(request.RoleName);
        existing.Reprioritise(request.Priority);

        var updated = await _repo.UpdateAsync(existing);

        return updated == null ? null : RoleMappingMapping.ToDto(updated);
    }
}
