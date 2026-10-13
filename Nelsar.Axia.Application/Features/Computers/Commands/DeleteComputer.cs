using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Computers.Commands;

/// <summary>False when there is no such account. Deleting a joined machine's account revokes
/// its trust and obliges it to rejoin the domain.</summary>
internal sealed record DeleteComputerCommand(Guid Id) : IRequest<bool>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ComputersManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class DeleteComputerCommandHandler : IRequestHandler<DeleteComputerCommand, bool>
{
    private readonly ILdapService _ldapService;
    private readonly ComputerDirectory _directory;
    private readonly PrivilegeGuard _guard;

    public DeleteComputerCommandHandler(ILdapService ldapService, ComputerDirectory directory, PrivilegeGuard guard)
    {
        _ldapService = ldapService;
        _directory = directory;
        _guard = guard;
    }

    public async Task<bool> Handle(DeleteComputerCommand request, CancellationToken cancellationToken)
    {
        var computer = await _directory.ResolveAsync(request.Id);
        if (computer == null)
            return false;

        await _guard.EnsureMayManageComputerAsync(computer, cancellationToken);

        return await _ldapService.DeleteComputerAsync(computer.Dn);
    }
}
