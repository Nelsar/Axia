using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>Null when no job carries that id.</summary>
internal sealed record GetBulkJobStatusQuery(string JobId) : IRequest<BulkOperationResponse<BulkUserResult>?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersBulk;
}

internal sealed class GetBulkJobStatusQueryHandler
    : IRequestHandler<GetBulkJobStatusQuery, BulkOperationResponse<BulkUserResult>?>
{
    private readonly IBulkJobStore _bulkJobStore;

    public GetBulkJobStatusQueryHandler(IBulkJobStore bulkJobStore)
    {
        _bulkJobStore = bulkJobStore;
    }

    public Task<BulkOperationResponse<BulkUserResult>?> Handle(
        GetBulkJobStatusQuery request,
        CancellationToken cancellationToken)
    {
        var job = _bulkJobStore.Get(request.JobId);
        if (job == null)
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
}
