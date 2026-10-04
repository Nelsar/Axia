using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>
/// Runs a batch to completion in the caller's own time and reports a per-item outcome.
///
/// Also the command the background worker dispatches for a queued job, which is why the batch
/// is validated here rather than only at the edge: that path never passes through MVC model
/// binding, so a controller-side guard would not have covered it.
///
/// It carries no <c>IRequirePermission</c> for the same reason: the worker dispatches it with no
/// signed-in caller to resolve permissions for, so a requirement here would refuse every queued
/// job. The check sits at both points where a person actually asks — <c>users.bulk</c> on the
/// synchronous endpoint, and on <see cref="EnqueueBulkUserJobCommand"/> for the queued one.
/// </summary>
internal sealed record ProcessBulkUsersCommand(IReadOnlyList<BulkUserRequest> Requests)
    : IRequest<BulkOperationResponse<BulkUserResult>>;

internal sealed class ProcessBulkUsersCommandValidator : AbstractValidator<ProcessBulkUsersCommand>
{
    public ProcessBulkUsersCommandValidator()
    {
        RuleFor(c => c.Requests)
            .NotEmpty()
            .WithMessage(_ => ApplicationMessages.BulkRequestEmpty());
    }
}

internal sealed class ProcessBulkUsersCommandHandler
    : IRequestHandler<ProcessBulkUsersCommand, BulkOperationResponse<BulkUserResult>>
{
    private readonly ILdapService _ldapService;
    private readonly ILogger<ProcessBulkUsersCommandHandler> _logger;

    public ProcessBulkUsersCommandHandler(
        ILdapService ldapService,
        ILogger<ProcessBulkUsersCommandHandler> logger)
    {
        _ldapService = ldapService;
        _logger = logger;
    }

    public async Task<BulkOperationResponse<BulkUserResult>> Handle(
        ProcessBulkUsersCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<BulkUserResult>();
        foreach (var request in command.Requests)
        {
            var item = new BulkUserResult
            {
                Email = request.Email
            };

            try
            {
                if (string.Equals(request.Operation, "update", StringComparison.OrdinalIgnoreCase))
                {
                    // Bulk items are not covered by the command's own validator, which only
                    // rejects an empty batch: one bad row must fail as one item, not take the
                    // other ninety-nine down with a 400. So this is still the only thing
                    // standing between pasted spreadsheet rows and the directory.
                    var email = Email.Create(request.Email);

                    var existing = await _ldapService.GetUserDetailsAsync(email.Value);
                    if (existing == null)
                        throw new DomainNotFoundException(ApplicationMessages.UserNotFoundForUpdate());

                    // NewEmail was declared on the request but never wired up, so a bulk sheet
                    // asking for an address change silently changed nothing.
                    var newEmail = string.IsNullOrWhiteSpace(request.NewEmail)
                        ? null
                        : Email.Create(request.NewEmail).Value;

                    await _ldapService.UpdateUserAsync(email.Value, request.Name, newEmail, request.Password);
                    var resultingEmail = newEmail ?? email.Value;

                    item.Status = BulkOperationStatus.Completed;
                    item.Message = "User updated successfully.";
                    item.User = new UserDto
                    {
                        Id = DeterministicIdFactory.BuildUserId(resultingEmail, existing.Dn, request.Name),
                        Email = resultingEmail,
                        Name = request.Name,
                        Role = UserRoles.User
                    };
                }
                else
                {
                    var email = Email.Create(request.Email);

                    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Password))
                        throw new DomainValidationException(ApplicationMessages.BulkCreateFieldsRequired());

                    var existing = await _ldapService.GetUserDetailsAsync(email.Value);
                    if (existing != null)
                        throw new DomainConflictException(ApplicationMessages.UserAlreadyExists());

                    await _ldapService.CreateUserAsync(request.Name, email.Value, request.Password);
                    item.Status = BulkOperationStatus.Completed;
                    item.Message = "User created successfully.";
                    item.User = new UserDto
                    {
                        Id = DeterministicIdFactory.BuildUserId(email.Value, string.Empty, request.Name),
                        Email = email.Value,
                        Name = request.Name,
                        Role = UserRoles.User
                    };
                }
            }
            catch (Exception ex)
            {
                item.Status = BulkOperationStatus.Failed;
                item.Message = ex.Message;
                _logger.LogWarning(ex, "Bulk user operation failed for [{Email}]", request.Email);
            }

            results.Add(item);
        }

        return new BulkOperationResponse<BulkUserResult>
        {
            Items = results,
            CompletedCount = results.Count(x => x.Status == BulkOperationStatus.Completed),
            FailedCount = results.Count(x => x.Status == BulkOperationStatus.Failed)
        };
    }
}
