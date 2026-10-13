using MediatR;
using Nelsar.Axia.Application.Features.Audit;
using Nelsar.Axia.Application.Features.Audit.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Modules;

/// <summary>The audit module.</summary>
internal sealed class AuditModule : IAuditModule
{
    private readonly ISender _sender;

    public AuditModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<PagedResponse<AuditEventDto>> GetRecentAsync(
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new GetRecentAuditEventsQuery(pageSize, pageToken), cancellationToken);

    public Task RecordRefusedAsync(
        string action,
        string request,
        IReadOnlyCollection<string> missingPermissions,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new RecordRefusedRequest(action, request, missingPermissions), cancellationToken);
}
