using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.OrganizationalUnits.Commands;

/// <summary>Applies the non-null parts of the request; null when there is no such unit. A
/// rename or move changes the unit's DN, and therefore the id it is addressed by.</summary>
internal sealed record UpdateOrganizationalUnitCommand(
    Guid Id,
    string? Name,
    string? ParentDistinguishedName,
    string? Description) : IRequest<OrganizationalUnitDto?>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.OrganizationalUnitsManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class UpdateOrganizationalUnitCommandHandler
    : IRequestHandler<UpdateOrganizationalUnitCommand, OrganizationalUnitDto?>
{
    private readonly ILdapService _ldapService;
    private readonly OrganizationalUnitDirectory _directory;
    private readonly PrivilegeGuard _guard;
    private readonly ILogger<UpdateOrganizationalUnitCommandHandler> _logger;

    public UpdateOrganizationalUnitCommandHandler(
        ILdapService ldapService,
        OrganizationalUnitDirectory directory,
        PrivilegeGuard guard,
        ILogger<UpdateOrganizationalUnitCommandHandler> logger)
    {
        _ldapService = ldapService;
        _directory = directory;
        _guard = guard;
        _logger = logger;
    }

    public async Task<OrganizationalUnitDto?> Handle(
        UpdateOrganizationalUnitCommand request,
        CancellationToken cancellationToken)
    {
        var current = await _directory.ResolveAsync(request.Id);
        if (current == null)
            return null;

        // A new name or parent re-addresses everything beneath the unit, so the unit is judged by
        // what it holds. A new description alone changes nothing below it.
        if (Readdresses(current.Dn, request))
            await _guard.EnsureMayReaddressUnitAsync(current.Dn, cancellationToken);

        var dn = await _ldapService.UpdateOrganizationalUnitAsync(
            current.Dn,
            request.Name,
            request.ParentDistinguishedName,
            // Null and empty mean different things: null leaves the description alone, empty
            // clears it. A DTO default of "" would silently wipe it on every rename.
            request.Description);

        _logger.LogInformation("Organizational unit updated: {Dn}", dn);

        var updated = DistinguishedName.Create(dn);
        return OrganizationalUnitDirectory.Map(new LdapOrganizationalUnit
        {
            Dn = updated.Value,
            Name = updated.Rdn,
            ParentDn = updated.Parent,
            Description = request.Description ?? current.Description
        });
    }

    /// <summary>The same comparison StandardLdapProvider.UpdateOrganizationalUnit makes to decide
    /// whether to rename or move.</summary>
    private static bool Readdresses(string currentDn, UpdateOrganizationalUnitCommand request)
    {
        var current = DistinguishedName.Create(currentDn);
        var renamed = !string.IsNullOrWhiteSpace(request.Name)
                      && !string.Equals(request.Name.Trim(), current.Rdn, StringComparison.OrdinalIgnoreCase);
        var moved = !string.IsNullOrWhiteSpace(request.ParentDistinguishedName)
                    && !string.Equals(request.ParentDistinguishedName.Trim(), current.Parent, StringComparison.OrdinalIgnoreCase);
        return renamed || moved;
    }
}
