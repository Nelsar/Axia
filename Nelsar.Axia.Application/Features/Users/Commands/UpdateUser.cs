using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>Null when there is no such account.</summary>
internal sealed record UpdateUserCommand(Guid Id, string Name, string? Email, string? Password) : IRequest<UserDto?>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersUpdate;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();

    /// <inheritdoc />
    public string? AuditDescription => string.IsNullOrEmpty(Password) ? null : "password reset";
}

internal sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

internal sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UserDto?>
{
    private readonly ILdapService _ldapService;
    private readonly IDomainEventPublisher _events;
    private readonly UserDirectory _directory;
    private readonly PrivilegeGuard _privilegeGuard;

    public UpdateUserCommandHandler(
        ILdapService ldapService,
        IDomainEventPublisher events,
        UserDirectory directory,
        PrivilegeGuard privilegeGuard)
    {
        _ldapService = ldapService;
        _events = events;
        _directory = directory;
        _privilegeGuard = privilegeGuard;
    }

    public async Task<UserDto?> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _directory.ResolveAsync(request.Id);
        if (user == null)
            return null;

        // users.update is what lets a caller reset a password, and on its own it said nothing
        // about whose: an editor could reset an administrator's and sign in as them. Refused
        // when the account can do anything the caller cannot. See PrivilegeGuard.
        await _privilegeGuard.EnsureMayManageAccountAsync(user.Dn, cancellationToken);

        // Validated and normalised here rather than inside the provider, so the address
        // actually written to the directory is byte-for-byte the one the new id derives from.
        var newEmail = string.IsNullOrWhiteSpace(request.Email) ? null : Email.Create(request.Email).Value;

        if (newEmail != null && !string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            await _directory.EnsureAddressIsFreeAsync(newEmail, user.Dn);

        // By DN: the entry just checked is the entry changed. The address would be searched for
        // again, and could find a different entry that shares it.
        await _ldapService.UpdateUserAsync(user.Dn, request.Name, newEmail, request.Password);

        var email = newEmail ?? user.Email;

        // Falls back to the login for the same reason the id derivation does: an account with no
        // mail is still addressed by something, and BuildUserId given an empty string invents a
        // random Guid rather than deriving one.
        var address = string.IsNullOrWhiteSpace(email) ? user.Username : email;

        // A new instance rather than a mutation: the object we just read is the very instance
        // handed to everyone else holding it from the cache, so mutating it would publish a
        // half-applied update to concurrent readers.
        var updated = new UserDto
        {
            // The id is derived from the address (see DeterministicIdFactory), so changing the
            // address changes the id. Returning the old one would hand back a handle that
            // resolves to nothing on the very next request.
            Id = DeterministicIdFactory.BuildUserId(address),
            Email = email,
            Username = user.Username,
            Name = request.Name,
            Role = user.Role,
            Dn = user.Dn,
            Groups = user.Groups
        };

        if (updated.Id != request.Id)
            await _directory.ForgetAsync(request.Id);

        await _directory.RememberAsync(updated);
        await _events.PublishAsync(new UserUpdated(updated.Id, updated.Email));
        return updated;
    }
}
