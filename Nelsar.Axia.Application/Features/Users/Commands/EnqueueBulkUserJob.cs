using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>Queues the batch and answers with the job id to poll.</summary>
internal sealed record EnqueueBulkUserJobCommand(IReadOnlyList<BulkUserRequest> Requests)
    : IRequest<string>, IRequireAdditionalPermissions, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersBulk;

    /// <summary>
    /// Refused here, at the moment a person asks, rather than left for the job to fail row by
    /// row later: a batch whose rows the caller may not perform is not something to queue.
    /// The job is checked again when it runs (see ProcessBulkUsersCommand).
    /// </summary>
    public IEnumerable<string> AdditionalPermissions => BulkUserOperations.PermissionsFor(Requests);

    /// <inheritdoc />
    public string AuditTarget => $"{Requests?.Count ?? 0} row(s)";
}

internal sealed class EnqueueBulkUserJobCommandValidator : AbstractValidator<EnqueueBulkUserJobCommand>
{
    public EnqueueBulkUserJobCommandValidator()
    {
        RuleFor(c => c.Requests)
            .NotEmpty()
            .WithMessage(_ => ApplicationMessages.BulkRequestEmpty());

        RuleForEach(c => c.Requests).NotNull();
    }
}

internal sealed class EnqueueBulkUserJobCommandHandler : IRequestHandler<EnqueueBulkUserJobCommand, string>
{
    private readonly IBulkJobQueue _bulkJobQueue;
    private readonly IBulkJobStore _bulkJobStore;
    private readonly ActingUser _currentUser;

    public EnqueueBulkUserJobCommandHandler(
        IBulkJobQueue bulkJobQueue,
        IBulkJobStore bulkJobStore,
        ActingUser currentUser)
    {
        _bulkJobQueue = bulkJobQueue;
        _bulkJobStore = bulkJobStore;
        _currentUser = currentUser;
    }

    public async Task<string> Handle(EnqueueBulkUserJobCommand request, CancellationToken cancellationToken)
    {
        var job = new BulkUserJob
        {
            Requests = request.Requests.ToList(),
            State = BulkJobState.Pending,

            // Captured here rather than left for the worker to work out, because by then there is
            // nothing left to work it out from. This is the same directory every other part of
            // this request is already using — the batch runs where the person who asked for it
            // was standing, not where the default happens to point.
            ProviderId = _currentUser.ProviderId,

            // Who the job runs as. The worker has no request to read a principal from, so without
            // this the batch ran as nobody: unchecked, and unattributed in the audit trail.
            RequestedBy = _currentUser.Subject,
            RequestedByDn = _currentUser.DistinguishedName
        };

        _bulkJobStore.Add(job);
        await _bulkJobQueue.EnqueueAsync(job, cancellationToken);
        return job.Id;
    }
}
