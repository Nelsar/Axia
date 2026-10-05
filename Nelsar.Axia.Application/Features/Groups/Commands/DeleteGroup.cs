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

    public DeleteGroupCommandHandler(ILdapService ldapService, GroupDirectory directory)
    {
        _ldapService = ldapService;
        _directory = directory;
    }

    public async Task<bool> Handle(DeleteGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _directory.ResolveAsync(request.Id);
        if (group == null)
            return false;

        return await _ldapService.DeleteGroupAsync(group.Dn);
    }
}
