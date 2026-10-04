using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Application.Features.Users.Commands;

/// <summary>Queues the batch and answers with the job id to poll.</summary>
internal sealed record EnqueueBulkUserJobCommand(IReadOnlyList<BulkUserRequest> Requests) : IRequest<string>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersBulk;
}

internal sealed class EnqueueBulkUserJobCommandValidator : AbstractValidator<EnqueueBulkUserJobCommand>
{
    public EnqueueBulkUserJobCommandValidator()
    {
        RuleFor(c => c.Requests)
            .NotEmpty()
            .WithMessage(_ => ApplicationMessages.BulkRequestEmpty());
    }
}

internal sealed class EnqueueBulkUserJobCommandHandler : IRequestHandler<EnqueueBulkUserJobCommand, string>
{
    private readonly IBulkJobQueue _bulkJobQueue;
    private readonly IBulkJobStore _bulkJobStore;
    private readonly ICurrentUser _currentUser;

    public EnqueueBulkUserJobCommandHandler(
        IBulkJobQueue bulkJobQueue,
        IBulkJobStore bulkJobStore,
        ICurrentUser currentUser)
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
            ProviderId = _currentUser.ProviderId
        };

        _bulkJobStore.Add(job);
        await _bulkJobQueue.EnqueueAsync(job, cancellationToken);
        return job.Id;
    }
}
