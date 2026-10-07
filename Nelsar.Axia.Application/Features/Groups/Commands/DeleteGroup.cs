using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Groups.Commands;

/// <summary>False when there is no such group.</summary>
internal sealed record DeleteGroupCommand(Guid Id) : IRequest<bool>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.GroupsManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class DeleteGroupCommandHandler : IRequestHandler<DeleteGroupCommand, bool>
{
    private readonly ILdapService _ldapService;
    private readonly GroupDirectory _directory;
    private readonly PrivilegeGuard _privilegeGuard;

    public DeleteGroupCommandHandler(ILdapService ldapService, GroupDirectory directory, PrivilegeGuard privilegeGuard)
    {
        _ldapService = ldapService;
        _directory = directory;
        _privilegeGuard = privilegeGuard;
    }

    public async Task<bool> Handle(DeleteGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _directory.ResolveAsync(request.Id);
        if (group == null)
            return false;

        // Deleting the group mapped to Admin takes the role from every administrator at once;
        // only a caller holding what it confers may. See PrivilegeGuard.
        await _privilegeGuard.EnsureMayManageGroupAsync(group, null, cancellationToken);

        return await _ldapService.DeleteGroupAsync(group.Dn);
    }
}
