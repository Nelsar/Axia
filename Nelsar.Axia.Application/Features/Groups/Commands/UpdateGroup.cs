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

/// <summary>Applies the non-null parts of the request; null when there is no such group. A
/// rename or move changes the group's DN, and therefore the id it is addressed by.</summary>
internal sealed record UpdateGroupCommand(
    Guid Id,
    string? Name,
    string? ParentDistinguishedName,
    string? Description,
    IEnumerable<string>? MemberDistinguishedNames) : IRequest<GroupDto?>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.GroupsManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();

    /// <inheritdoc />
    public string? AuditDescription => MemberDistinguishedNames is null ? null : $"members replaced with {string.Join(" | ", MemberDistinguishedNames)}";
}

internal sealed class UpdateGroupCommandHandler : IRequestHandler<UpdateGroupCommand, GroupDto?>
{
    private readonly ILdapService _ldapService;
    private readonly GroupDirectory _directory;
    private readonly PrivilegeGuard _privilegeGuard;
    private readonly ILogger<UpdateGroupCommandHandler> _logger;

    public UpdateGroupCommandHandler(
        ILdapService ldapService,
        GroupDirectory directory,
        PrivilegeGuard privilegeGuard,
        ILogger<UpdateGroupCommandHandler> logger)
    {
        _ldapService = ldapService;
        _directory = directory;
        _privilegeGuard = privilegeGuard;
        _logger = logger;
    }

    public async Task<GroupDto?> Handle(UpdateGroupCommand request, CancellationToken cancellationToken)
    {
        var current = await _directory.ResolveAsync(request.Id);
        if (current == null)
            return null;

        // Both what the group confers now — editing the membership of the group mapped to Admin
        // is how one joins it — and what it would confer under its new name. See PrivilegeGuard.
        await _privilegeGuard.EnsureMayManageGroupAsync(
            current,
            string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            cancellationToken);

        // Null means "leave the membership alone"; a list that was given has to be usable, and
        // emptying a groupOfNames is not. Not a validator rule: it only applies when the caller
        // supplied a list at all, and the check reads better next to the null it distinguishes.
        IReadOnlyList<string>? members = null;
        if (request.MemberDistinguishedNames != null)
        {
            members = GroupDirectory.NormaliseMembers(request.MemberDistinguishedNames);
            if (members.Count == 0)
                throw new DomainValidationException(ApplicationMessages.GroupMembersRequired());
        }

        var dn = await _ldapService.UpdateGroupAsync(
            current.Dn,
            request.Name,
            request.ParentDistinguishedName,
            // Null and empty mean different things: null leaves the description alone, empty
            // clears it. A DTO default of "" would silently wipe it on every rename.
            request.Description,
            members);

        _logger.LogInformation("Group updated: {Dn}", dn);

        var updated = DistinguishedName.Create(dn);
        return GroupDirectory.Map(new LdapGroup
        {
            Dn = updated.Value,
            Name = updated.Rdn,
            ParentDn = updated.Parent,
            Description = request.Description ?? current.Description,
            Members = members ?? current.Members
        });
    }
}
