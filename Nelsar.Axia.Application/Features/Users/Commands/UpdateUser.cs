using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>Null when there is no such account.</summary>
internal sealed record UpdateUserCommand(Guid Id, string Name, string? Email, string? Password) : IRequest<UserDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersUpdate;
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

    public UpdateUserCommandHandler(
        ILdapService ldapService,
        IDomainEventPublisher events,
        UserDirectory directory)
    {
        _ldapService = ldapService;
        _events = events;
        _directory = directory;
    }

    public async Task<UserDto?> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _directory.ResolveAsync(request.Id);
        if (user == null)
            return null;

        // Validated and normalised here rather than inside the provider, so the address
        // actually written to the directory is byte-for-byte the one the new id derives from.
        var newEmail = string.IsNullOrWhiteSpace(request.Email) ? null : Email.Create(request.Email).Value;

        await _ldapService.UpdateUserAsync(user.DirectoryAddress, request.Name, newEmail, request.Password);

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
