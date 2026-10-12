using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Contracts.Diagnostics;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;
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
/// It requires users.bulk, and on top of that the single-account permission for every kind of
/// row it holds — users.create for create rows, users.update for update rows. users.bulk used to
/// be the whole check, and because update rows set passwords, holding it was enough to reset any
/// account's password without users.update. The worker dispatches this as the person who queued
/// the job (see BackgroundPrincipal), so a queued batch is checked the same way, at the time it
/// runs.
/// </summary>
/// <param name="Queued">
/// Whether the worker is running a queued job, rather than a caller waiting on the answer — which
/// decides how many rows the batch may hold.
/// </param>
internal sealed record ProcessBulkUsersCommand(IReadOnlyList<BulkUserRequest> Requests, bool Queued = false)
    : IRequest<BulkOperationResponse<BulkUserResult>>, IRequireAdditionalPermissions, IAuditedCommand
{
    /// <summary>
    /// The most rows one synchronous batch may hold. It runs on the request's thread, a directory
    /// round trip or two per row, while the caller waits; nothing used to bound it, so one request
    /// could hold a thread for as long as it cared to make the batch. Larger batches are queued.
    /// </summary>
    public const int MaximumSynchronousRows = 100;

    /// <summary>
    /// The most rows one queued batch may hold: as many logins as one preview of the generator
    /// produces (GenerateUserPatternsQuery.MaximumItems), so whatever it previews can be created
    /// in one job.
    /// </summary>
    public const int MaximumQueuedRows = 10_000;

    public int MaximumRows => Queued ? MaximumQueuedRows : MaximumSynchronousRows;

    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersBulk;

    /// <inheritdoc />
    public IEnumerable<string> AdditionalPermissions => BulkUserOperations.PermissionsFor(Requests);

    /// <inheritdoc />
    public string AuditTarget => $"{Requests?.Count ?? 0} row(s)";
}

internal sealed class ProcessBulkUsersCommandValidator : AbstractValidator<ProcessBulkUsersCommand>
{
    public ProcessBulkUsersCommandValidator()
    {
        RuleFor(c => c.Requests)
            .NotEmpty()
            .WithMessage(_ => ApplicationMessages.BulkRequestEmpty());

        RuleFor(c => c.Requests)
            .Must((command, rows) => rows.Count <= command.MaximumRows)
            .When(c => c.Requests is not null)
            .WithMessage(c => c.Queued
                ? ApplicationMessages.BulkRequestTooLarge(c.MaximumRows)
                : ApplicationMessages.BulkSynchronousRequestTooLarge(c.MaximumRows));

        // A null row has nothing to report a per-item outcome against, so it fails the batch
        // rather than the handler.
        RuleForEach(c => c.Requests).NotNull();
    }
}

internal sealed class ProcessBulkUsersCommandHandler
    : IRequestHandler<ProcessBulkUsersCommand, BulkOperationResponse<BulkUserResult>>
{
    private readonly ILdapService _ldapService;
    private readonly IAuditService _audit;
    private readonly ActingUser _actingUser;
    private readonly PrivilegeGuard _privilegeGuard;
    private readonly UserDirectory _directory;
    private readonly ILogger<ProcessBulkUsersCommandHandler> _logger;

    public ProcessBulkUsersCommandHandler(
        ILdapService ldapService,
        IAuditService audit,
        ActingUser actingUser,
        PrivilegeGuard privilegeGuard,
        UserDirectory directory,
        ILogger<ProcessBulkUsersCommandHandler> logger)
    {
        _ldapService = ldapService;
        _audit = audit;
        _actingUser = actingUser;
        _privilegeGuard = privilegeGuard;
        _directory = directory;
        _logger = logger;
    }

    public async Task<BulkOperationResponse<BulkUserResult>> Handle(
        ProcessBulkUsersCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<BulkUserResult>();
        foreach (var request in command.Requests)
        {
            var operation = BulkUserOperations.Of(request);
            var item = new BulkUserResult
            {
                Email = request.Email
            };

            string? dn = null;

            try
            {
                if (operation == BulkUserOperation.Update)
                {
                    // Bulk items are not covered by the command's own validator, which only
                    // rejects an empty batch: one bad row must fail as one item, not take the
                    // other ninety-nine down with a 400. So this is still the only thing
                    // standing between pasted spreadsheet rows and the directory.
                    var email = Email.Create(request.Email);

                    var existing = await _ldapService.GetUserDetailsAsync(email.Value);
                    if (existing == null)
                        throw new DomainNotFoundException(ApplicationMessages.UserNotFoundForUpdate());

                    dn = existing.Dn;

                    // The same check a single update makes, row by row: an update row can set a
                    // password, and holding users.update must not make every administrator's
                    // password one of them. A refusal fails this row, like any other bad row.
                    await _privilegeGuard.EnsureMayManageAccountAsync(existing.Dn, cancellationToken);

                    // NewEmail was declared on the request but never wired up, so a bulk sheet
                    // asking for an address change silently changed nothing.
                    var newEmail = string.IsNullOrWhiteSpace(request.NewEmail)
                        ? null
                        : Email.Create(request.NewEmail).Value;

                    if (newEmail != null && !string.Equals(newEmail, email.Value, StringComparison.OrdinalIgnoreCase))
                        await _directory.EnsureAddressIsFreeAsync(newEmail, existing.Dn);

                    // By the DN just checked, not the address it was found by.
                    await _ldapService.UpdateUserAsync(existing.Dn, request.Name, newEmail, request.Password);
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
                else if (operation == BulkUserOperation.Create)
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
                else
                {
                    throw new DomainValidationException(
                        ApplicationMessages.BulkOperationUnknown(request.Operation ?? string.Empty));
                }
            }
            catch (Exception ex)
            {
                item.Status = BulkOperationStatus.Failed;
                item.Message = ex.Message;
                _logger.LogWarning(ex, "Bulk user operation failed for {Login}.", LogMask.Login(request.Email));
            }

            await AuditRowAsync(operation, request, item, dn);
            results.Add(item);
        }

        return new BulkOperationResponse<BulkUserResult>
        {
            Items = results,
            CompletedCount = results.Count(x => x.Status == BulkOperationStatus.Completed),
            FailedCount = results.Count(x => x.Status == BulkOperationStatus.Failed)
        };
    }

    /// <summary>
    /// One audit entry per row, under the name of whoever submitted the batch. The batch as a
    /// whole is audited by AuditBehaviour; this is what says which accounts it touched, and
    /// whether a row reset someone's password — the change that most needs to be traceable.
    /// </summary>
    private Task AuditRowAsync(BulkUserOperation operation, BulkUserRequest request, BulkUserResult item, string? dn)
    {
        var action = operation switch
        {
            BulkUserOperation.Create => "BulkCreateUser",
            BulkUserOperation.Update => "BulkUpdateUser",
            _ => "BulkUnknownOperation"
        };

        var details = new List<string> { $"target={request.Email}" };
        if (operation == BulkUserOperation.Update && !string.IsNullOrWhiteSpace(request.Password))
            details.Add("password reset");
        if (operation == BulkUserOperation.Update && !string.IsNullOrWhiteSpace(request.NewEmail))
            details.Add($"new email={request.NewEmail}");
        details.Add(AuditDetails.Directory(_actingUser.ProviderId));
        if (item.Status == BulkOperationStatus.Failed && !string.IsNullOrWhiteSpace(item.Message))
            details.Add($"error={item.Message}");

        return _audit.LogModificationAsync(
            item.User?.Id,
            dn,
            _actingUser.Subject ?? string.Empty,
            action,
            item.Status == BulkOperationStatus.Completed,
            string.Join("; ", details));
    }
}
