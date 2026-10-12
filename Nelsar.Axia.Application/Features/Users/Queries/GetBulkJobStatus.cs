using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>
/// Null when no job carries that id, and equally when one does but it is not the caller's: queued
/// by someone else, or in another directory. A job's results name every account in the batch and
/// why each row failed, and anyone holding users.bulk who learned a job id used to read them all.
/// Answering the same way for both keeps the id from confirming that someone else's job exists.
/// </summary>
internal sealed record GetBulkJobStatusQuery(string JobId) : IRequest<BulkOperationResponse<BulkUserResult>?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersBulk;
}

internal sealed class GetBulkJobStatusQueryHandler
    : IRequestHandler<GetBulkJobStatusQuery, BulkOperationResponse<BulkUserResult>?>
{
    private readonly IBulkJobStore _bulkJobStore;
    private readonly ActingUser _actingUser;

    public GetBulkJobStatusQueryHandler(IBulkJobStore bulkJobStore, ActingUser actingUser)
    {
        _bulkJobStore = bulkJobStore;
        _actingUser = actingUser;
    }

    public Task<BulkOperationResponse<BulkUserResult>?> Handle(
        GetBulkJobStatusQuery request,
        CancellationToken cancellationToken)
    {
        var job = _bulkJobStore.Get(request.JobId);
        if (job == null || !IsCallers(job))
            return Task.FromResult<BulkOperationResponse<BulkUserResult>?>(null);

        return Task.FromResult<BulkOperationResponse<BulkUserResult>?>(new BulkOperationResponse<BulkUserResult>
        {
            JobId = job.Id,
            State = BulkJobStateWire.For(job.State),
            Items = job.Results,
            CompletedCount = job.Results.Count(x => x.Status == BulkOperationStatus.Completed),
            FailedCount = job.Results.Count(x => x.Status == BulkOperationStatus.Failed),
            ErrorMessage = job.ErrorMessage
        });
    }

    /// <summary>
    /// Queued by the entry the caller's session is bound to, in the directory it is bound to. The
    /// DN rather than the subject, since the DN is what a session is (see AxiaClaims), and the
    /// directory too, since one DN can exist in two of them.
    /// </summary>
    private bool IsCallers(BulkUserJob job) =>
        job.ProviderId == _actingUser.ProviderId
        && DistinguishedName.TryCreate(job.RequestedByDn, out var owner)
        && DistinguishedName.TryCreate(_actingUser.DistinguishedName, out var caller)
        && string.Equals(owner!.Normalized, caller!.Normalized, StringComparison.OrdinalIgnoreCase);
}
