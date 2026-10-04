using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Computers.Commands;

/// <summary>False when there is no such account. Deleting a joined machine's account revokes
/// its trust and obliges it to rejoin the domain.</summary>
internal sealed record DeleteComputerCommand(Guid Id) : IRequest<bool>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ComputersManage;
}

internal sealed class DeleteComputerCommandHandler : IRequestHandler<DeleteComputerCommand, bool>
{
    private readonly ILdapService _ldapService;
    private readonly ComputerDirectory _directory;

    public DeleteComputerCommandHandler(ILdapService ldapService, ComputerDirectory directory)
    {
        _ldapService = ldapService;
        _directory = directory;
    }

    public async Task<bool> Handle(DeleteComputerCommand request, CancellationToken cancellationToken)
    {
        var computer = await _directory.ResolveAsync(request.Id);
        if (computer == null)
            return false;

        return await _ldapService.DeleteComputerAsync(computer.Dn);
    }
}
