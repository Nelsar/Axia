using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Groups.Commands;

/// <summary>Creates the group under the requested parent, or under the base DN when none is
/// given. An entry already sitting at that DN is a conflict, raised by the adapter.</summary>
internal sealed record CreateGroupCommand(
    string Name,
    string ParentDistinguishedName,
    string Description,
    IEnumerable<string> MemberDistinguishedNames) : IRequest<GroupDto>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.GroupsManage;

    /// <inheritdoc />
    public string AuditTarget => Name;

    /// <inheritdoc />
    public string? AuditDescription => $"members={string.Join(" | ", MemberDistinguishedNames ?? [])}";
}

internal sealed class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(c => c.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => ApplicationMessages.GroupNameRequired());

        // Refused here rather than at the directory: a groupOfNames must carry a member, so an
        // empty list is a request the schema cannot satisfy, and saying which requirement was
        // missed beats relaying an object-class violation.
        RuleFor(c => c.MemberDistinguishedNames)
            .Must(members => GroupDirectory.NormaliseMembers(members).Count > 0)
            .WithMessage(_ => ApplicationMessages.GroupMembersRequired());
    }
}

internal sealed class CreateGroupCommandHandler : IRequestHandler<CreateGroupCommand, GroupDto>
{
    private readonly ILdapService _ldapService;
    private readonly PrivilegeGuard _privilegeGuard;
    private readonly ILogger<CreateGroupCommandHandler> _logger;

    public CreateGroupCommandHandler(
        ILdapService ldapService,
        PrivilegeGuard privilegeGuard,
        ILogger<CreateGroupCommandHandler> logger)
    {
        _ldapService = ldapService;
        _privilegeGuard = privilegeGuard;
        _logger = logger;
    }

    public async Task<GroupDto> Handle(CreateGroupCommand request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var members = GroupDirectory.NormaliseMembers(request.MemberDistinguishedNames);
        var description = (request.Description ?? string.Empty).Trim();

        // A new group under a mapped group's name, with the caller as a member, is the Admin role
        // for anyone who can create groups. Refused unless the caller already holds what that
        // name is mapped to. See PrivilegeGuard.
        await _privilegeGuard.EnsureMayManageGroupAsync(null, name, cancellationToken);

        var dn = await _ldapService.CreateGroupAsync(
            name, request.ParentDistinguishedName ?? string.Empty, description, members);

        _logger.LogInformation("Group created: {Dn}", dn);

        // Built from the DN the directory actually wrote: a blank parent resolves to the
        // configured base DN, and only the adapter knows what that is.
        var created = DistinguishedName.Create(dn);
        return GroupDirectory.Map(new LdapGroup
        {
            Dn = created.Value,
            Name = created.Rdn,
            ParentDn = created.Parent,
            Description = description,
            Members = members
        });
    }
}
