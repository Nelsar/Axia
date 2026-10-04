using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.OrganizationalUnits.Commands;

/// <summary>Creates the unit under the requested parent, or under the base DN when none is
/// given. An entry already sitting there is a conflict, raised by the adapter.</summary>
internal sealed record CreateOrganizationalUnitCommand(
    string Name,
    string ParentDistinguishedName,
    string Description) : IRequest<OrganizationalUnitDto>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.OrganizationalUnitsManage;
}

internal sealed class CreateOrganizationalUnitCommandValidator : AbstractValidator<CreateOrganizationalUnitCommand>
{
    public CreateOrganizationalUnitCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => ApplicationMessages.OrganizationalUnitNameRequired());
    }
}

internal sealed class CreateOrganizationalUnitCommandHandler
    : IRequestHandler<CreateOrganizationalUnitCommand, OrganizationalUnitDto>
{
    private readonly ILdapService _ldapService;
    private readonly ILogger<CreateOrganizationalUnitCommandHandler> _logger;

    public CreateOrganizationalUnitCommandHandler(
        ILdapService ldapService,
        ILogger<CreateOrganizationalUnitCommandHandler> logger)
    {
        _ldapService = ldapService;
        _logger = logger;
    }

    public async Task<OrganizationalUnitDto> Handle(
        CreateOrganizationalUnitCommand request,
        CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var description = (request.Description ?? string.Empty).Trim();

        var dn = await _ldapService.CreateOrganizationalUnitAsync(
            name, request.ParentDistinguishedName ?? string.Empty, description);

        _logger.LogInformation("Organizational unit created: {Dn}", dn);

        // Built from the DN the directory actually wrote, not from the request: a blank parent
        // resolves to the configured base DN, and only the adapter knows what that is.
        var created = DistinguishedName.Create(dn);
        return OrganizationalUnitDirectory.Map(new LdapOrganizationalUnit
        {
            Dn = created.Value,
            Name = created.Rdn,
            ParentDn = created.Parent,
            Description = description
        });
    }
}
