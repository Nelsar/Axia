using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>
/// Creates a directory account.
///
/// Deliberately carries no <c>IRequirePermission</c>, unlike every other use case a guarded
/// endpoint dispatches, because two endpoints dispatch this one and they disagree about who may
/// call it: <c>POST /api/v1/users</c> requires <see cref="Permissions.UsersCreate"/>, while
/// <c>POST /api/v1/auth/register</c> is open to anyone who can reach it. Attaching the permission
/// here would silently close self-registration; leaving it off keeps both endpoints behaving
/// exactly as they do today.
///
/// That the same operation is reachable both ways is worth a decision rather than a comment. If
/// self-registration is intended, this should become two commands — a guarded CreateUserCommand
/// and an anonymous RegisterUserCommand — so each states its own rule. If it is not, the
/// register endpoint should go, and this can then carry the permission like the rest.
/// </summary>
internal sealed record CreateUserCommand(string Email, string Name, string Password) : IRequest<UserDto>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditTarget => Email;
}

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        // Presence is checked here; the address's shape is still Email.Create's call in the
        // handler, so the rule lives in the domain type rather than being restated as a regex.
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}

internal sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly ILdapService _ldapService;
    private readonly IDomainEventPublisher _events;
    private readonly UserDirectory _directory;

    public CreateUserCommandHandler(
        ILdapService ldapService,
        IDomainEventPublisher events,
        UserDirectory directory)
    {
        _ldapService = ldapService;
        _events = events;
        _directory = directory;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = Email.Create(request.Email);

        var existing = await _ldapService.GetUserDetailsAsync(email.Value);
        if (existing != null)
            throw new DomainConflictException(ApplicationMessages.UserAlreadyExistsInDirectory());

        await _ldapService.CreateUserAsync(request.Name, email.Value, request.Password);

        var userDto = new UserDto
        {
            Id = DeterministicIdFactory.BuildUserId(email.Value, string.Empty, request.Name),
            Email = email.Value,
            Name = request.Name,
            Role = UserRoles.User
        };

        await _directory.RememberAsync(userDto);
        await _events.PublishAsync(new UserCreated(userDto.Id, email.Value));
        return userDto;
    }
}
