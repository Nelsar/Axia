using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.OrganizationalUnits.Commands;

/// <summary>False when there is no such unit. A unit that still holds entries is a conflict
/// raised by the adapter, never a silent recursive delete.</summary>
internal sealed record DeleteOrganizationalUnitCommand(Guid Id) : IRequest<bool>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.OrganizationalUnitsManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class DeleteOrganizationalUnitCommandHandler
    : IRequestHandler<DeleteOrganizationalUnitCommand, bool>
{
    private readonly ILdapService _ldapService;
    private readonly OrganizationalUnitDirectory _directory;

    public DeleteOrganizationalUnitCommandHandler(ILdapService ldapService, OrganizationalUnitDirectory directory)
    {
        _ldapService = ldapService;
        _directory = directory;
    }

    public async Task<bool> Handle(DeleteOrganizationalUnitCommand request, CancellationToken cancellationToken)
    {
        var unit = await _directory.ResolveAsync(request.Id);
        if (unit == null)
            return false;

        return await _ldapService.DeleteOrganizationalUnitAsync(unit.Dn);
    }
}
