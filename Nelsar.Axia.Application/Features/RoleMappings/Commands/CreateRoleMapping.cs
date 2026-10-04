using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RoleMappings.Commands;

/// <summary>A null ProviderId is a global mapping, not a missing one.</summary>
internal sealed record CreateRoleMappingCommand(
    Guid? ProviderId,
    string GroupName,
    string RoleName,
    int Priority) : IRequest<RoleMappingDto>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RolesManage;
}

internal sealed class CreateRoleMappingCommandValidator : AbstractValidator<CreateRoleMappingCommand>
{
    public CreateRoleMappingCommandValidator()
    {
        RuleFor(c => c.GroupName).NotEmpty();
        RuleFor(c => c.RoleName).NotEmpty();
    }
}

internal sealed class CreateRoleMappingCommandHandler : IRequestHandler<CreateRoleMappingCommand, RoleMappingDto>
{
    private readonly IRoleMappingRepository _repo;

    public CreateRoleMappingCommandHandler(IRoleMappingRepository repo) => _repo = repo;

    public async Task<RoleMappingDto> Handle(CreateRoleMappingCommand request, CancellationToken cancellationToken)
    {
        var created = await _repo.AddAsync(RoleMapping.Create(
            request.ProviderId,
            request.GroupName,
            request.RoleName,
            request.Priority));

        return RoleMappingMapping.ToDto(created);
    }
}
