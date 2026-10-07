using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Contracts.Diagnostics;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>
/// Null when the directory refuses the credentials or has no entry to describe the account.
/// The caller turns that into 401; which of the two it was is deliberately not distinguished
/// on the wire.
/// </summary>
internal sealed record AuthenticateCommand(string Login, string Password) : IRequest<AuthenticateResponse?>;

internal sealed class AuthenticateCommandValidator : AbstractValidator<AuthenticateCommand>
{
    public AuthenticateCommandValidator()
    {
        // Presence only, never format. An Active Directory account is not required to carry
        // mail or userPrincipalName at all, and validating the login as an address rejected
        // those users with a 400 before the directory was ever asked — see the note on
        // AuthenticateRequest.Email, which this command is built from.
        RuleFor(c => c.Login).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}

internal sealed class AuthenticateCommandHandler : IRequestHandler<AuthenticateCommand, AuthenticateResponse?>
{
    private readonly ILdapService _ldapService;
    private readonly ITokenService _tokenService;
    private readonly IDomainEventPublisher _events;
    private readonly UserDirectory _directory;
    private readonly IPermissionResolver _permissions;
    private readonly ActingUser _actingUser;
    private readonly ILogger<AuthenticateCommandHandler> _logger;

    public AuthenticateCommandHandler(
        ILdapService ldapService,
        ITokenService tokenService,
        IDomainEventPublisher events,
        UserDirectory directory,
        IPermissionResolver permissions,
        ActingUser actingUser,
        ILogger<AuthenticateCommandHandler> logger)
    {
        _ldapService = ldapService;
        _tokenService = tokenService;
        _events = events;
        _directory = directory;
        _permissions = permissions;
        _actingUser = actingUser;
        _logger = logger;
    }

    public async Task<AuthenticateResponse?> Handle(AuthenticateCommand request, CancellationToken cancellationToken)
    {
        var boundDn = await _ldapService.AuthenticateAsync(request.Login, request.Password);
        if (boundDn is null)
        {
            // Masked and structured. Interpolating the login put it in the message text, where it
            // is neither redactable nor searchable — and a failed-login line is precisely the one
            // that accumulates: every typo, every brute-force attempt, every expired password,
            // each writing a real person's account name into a log that outlives the incident.
            // The audit trail records the actor in full; this is the diagnostic log, which does
            // not need to.
            _logger.LogWarning("LDAP authentication failed for {Login}.", LogMask.Login(request.Login));
            await _events.PublishAsync(new UserAuthenticationFailed(request.Login, "Invalid credentials"));
            return null;
        }

        // Read at the DN the password was proven against, not looked up by the login again. A
        // login can be shared by two entries, and a second search could return the other one —
        // whose name, address and groups the session would then have carried. Everything below
        // describes the entry that was bound to, and nothing else.
        var details = await _ldapService.GetUserByDnAsync(boundDn);
        if (details == null)
        {
            _logger.LogWarning("Credentials accepted but no directory entry describes {Login}.",
                LogMask.Login(request.Login));
            return null;
        }

        var role = await _ldapService.GetUserRoleAsync(details);
        var dn = details.Dn;

        // Derived from the entry the directory returned, not from what was typed at the login
        // form. Those differ whenever an account has no mail and is reached by something other
        // than its cn — and when they did, the id issued here disagreed with the id the same
        // account got from the listing, so the token named a user that GET /api/v1/users/{id}
        // could not resolve. DeterministicIdFactory's contract is that one LDAP identity maps
        // to one id whichever path resolved it; this is that contract holding for login too.
        var username = string.IsNullOrWhiteSpace(details.Username) ? request.Login : details.Username;
        var address = string.IsNullOrWhiteSpace(details.Email) ? username : details.Email;

        var userDto = new UserDto
        {
            Id = DeterministicIdFactory.BuildUserId(address, dn, request.Login),
            Email = details.Email,
            Username = username,
            Name = string.IsNullOrWhiteSpace(details.DisplayName) ? username : details.DisplayName,
            Role = role,
            Dn = dn,
            Groups = details.Groups
        };

        await _directory.RememberAsync(userDto);

        // Resolved the way every later request will resolve them — for this entry, in the directory
        // being signed in to — so the console shows from the start what the API will allow, rather
        // than guessing from Role, which is one role where the API grants the union of all of them.
        var held = await _permissions.ResolveAsync(dn, _actingUser.ProviderId, cancellationToken);

        var response = new AuthenticateResponse
        {
            Token = _tokenService.GenerateToken(userDto),
            User = userDto,
            Permissions = Permissions.All.Where(held.Contains).ToList()
        };

        // The address rather than Email: an account without mail would otherwise be audited as
        // an empty string, and the audit trail is the one place that must always name someone.
        await _events.PublishAsync(new UserAuthenticationSucceeded(userDto.DirectoryAddress, dn));

        return response;
    }
}
