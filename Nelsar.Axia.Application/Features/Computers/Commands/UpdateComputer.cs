using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Computers.Commands;

/// <summary>Applies the non-null parts of the request; null when there is no such account.
/// A rename or move changes the DN, and therefore the id it is addressed by.</summary>
internal sealed record UpdateComputerCommand(
    Guid Id,
    string? Name,
    string? ParentDistinguishedName,
    string? Description,
    string? DnsHostName,
    bool? Enabled) : IRequest<ComputerDto?>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ComputersManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();

    /// <inheritdoc />
    public string? AuditDescription => Enabled is { } enabled ? (enabled ? "enabled" : "disabled") : null;
}

internal sealed class UpdateComputerCommandHandler : IRequestHandler<UpdateComputerCommand, ComputerDto?>
{
    private readonly ILdapService _ldapService;
    private readonly ComputerDirectory _directory;
    private readonly ILogger<UpdateComputerCommandHandler> _logger;

    public UpdateComputerCommandHandler(
        ILdapService ldapService,
        ComputerDirectory directory,
        ILogger<UpdateComputerCommandHandler> logger)
    {
        _ldapService = ldapService;
        _directory = directory;
        _logger = logger;
    }

    public async Task<ComputerDto?> Handle(UpdateComputerCommand request, CancellationToken cancellationToken)
    {
        var current = await _directory.ResolveAsync(request.Id);
        if (current == null)
            return null;

        var dn = await _ldapService.UpdateComputerAsync(
            current.Dn,
            request.Name,
            request.ParentDistinguishedName,
            // Null and empty mean different things here, as they do for a group's description:
            // null leaves the value alone, empty clears it.
            request.Description,
            request.DnsHostName,
            request.Enabled);

        _logger.LogInformation("Computer updated: {Dn}", dn);

        var updated = DistinguishedName.Create(dn);
        return ComputerDirectory.Map(new LdapComputer
        {
            Dn = updated.Value,
            Name = updated.Rdn,
            ParentDn = updated.Parent,
            Description = request.Description ?? current.Description,
            DnsHostName = request.DnsHostName ?? current.DnsHostName,
            OperatingSystem = current.OperatingSystem,
            OperatingSystemVersion = current.OperatingSystemVersion,
            Enabled = request.Enabled ?? current.Enabled
        });
    }
}
