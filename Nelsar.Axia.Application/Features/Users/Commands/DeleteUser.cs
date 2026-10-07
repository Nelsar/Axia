using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>False when there is no such account.</summary>
internal sealed record DeleteUserCommand(Guid Id) : IRequest<bool>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersDelete;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, bool>
{
    private readonly ILdapService _ldapService;
    private readonly UserDirectory _directory;
    private readonly PrivilegeGuard _privilegeGuard;

    public DeleteUserCommandHandler(ILdapService ldapService, UserDirectory directory, PrivilegeGuard privilegeGuard)
    {
        _ldapService = ldapService;
        _directory = directory;
        _privilegeGuard = privilegeGuard;
    }

    public async Task<bool> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _directory.ResolveAsync(request.Id);
        if (user == null)
            return false;

        // A deleter may not remove an account that can do more than they can — every
        // administrator, for a holder of users.delete alone. See PrivilegeGuard.
        await _privilegeGuard.EnsureMayManageAccountAsync(user.Dn, cancellationToken);

        // By DN, so the entry checked is the entry removed; see UpdateUserCommandHandler.
        var deleted = await _ldapService.DeleteUserAsync(user.Dn);
        if (deleted)
            await _directory.ForgetAsync(request.Id);

        return deleted;
    }
}
