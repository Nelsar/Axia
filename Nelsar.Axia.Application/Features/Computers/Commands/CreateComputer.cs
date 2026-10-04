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

namespace Nelsar.Axia.Application.Features.Computers.Commands;

/// <summary>Pre-stages an account entry; the machine still joins the domain itself.</summary>
internal sealed record CreateComputerCommand(
    string Name,
    string ParentDistinguishedName,
    string Description,
    string DnsHostName) : IRequest<ComputerDto>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ComputersManage;
}

internal sealed class CreateComputerCommandValidator : AbstractValidator<CreateComputerCommand>
{
    public CreateComputerCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => ApplicationMessages.ComputerNameRequired());
    }
}

internal sealed class CreateComputerCommandHandler : IRequestHandler<CreateComputerCommand, ComputerDto>
{
    private readonly ILdapService _ldapService;
    private readonly ILogger<CreateComputerCommandHandler> _logger;

    public CreateComputerCommandHandler(ILdapService ldapService, ILogger<CreateComputerCommandHandler> logger)
    {
        _ldapService = ldapService;
        _logger = logger;
    }

    public async Task<ComputerDto> Handle(CreateComputerCommand request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var description = (request.Description ?? string.Empty).Trim();
        var dnsHostName = (request.DnsHostName ?? string.Empty).Trim();

        var dn = await _ldapService.CreateComputerAsync(
            name, request.ParentDistinguishedName ?? string.Empty, description, dnsHostName);

        _logger.LogInformation("Computer created: {Dn}", dn);

        // Built from the DN the directory actually wrote: a blank parent resolves to the
        // configured base DN, and only the adapter knows what that is.
        var created = DistinguishedName.Create(dn);
        return ComputerDirectory.Map(new LdapComputer
        {
            Dn = created.Value,
            Name = created.Rdn,
            ParentDn = created.Parent,
            Description = description,
            DnsHostName = dnsHostName,

            // A freshly written account is enabled: the adapter sets the trust-account flag
            // without ACCOUNTDISABLE. The operating system fields stay empty until a machine
            // joins and fills them in itself — guessing at them here would invent inventory data.
            Enabled = true
        });
    }
}
