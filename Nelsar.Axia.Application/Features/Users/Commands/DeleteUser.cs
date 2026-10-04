using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>False when there is no such account.</summary>
internal sealed record DeleteUserCommand(Guid Id) : IRequest<bool>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersDelete;
}

internal sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, bool>
{
    private readonly ILdapService _ldapService;
    private readonly UserDirectory _directory;

    public DeleteUserCommandHandler(ILdapService ldapService, UserDirectory directory)
    {
        _ldapService = ldapService;
        _directory = directory;
    }

    public async Task<bool> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _directory.ResolveAsync(request.Id);
        if (user == null)
            return false;

        var deleted = await _ldapService.DeleteUserAsync(user.DirectoryAddress);
        if (deleted)
            await _directory.ForgetAsync(request.Id);

        return deleted;
    }
}
