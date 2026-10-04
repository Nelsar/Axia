using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common.Validation;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.Audit.Queries;

/// <summary>
/// The read half of the audit trail. The write half is not a use case and stays on
/// <see cref="Application.Interfaces.IAuditService"/>: entries are appended by
/// AuditTrailEventHandler in reaction to domain events, never by a request from a client.
/// </summary>
internal sealed record GetRecentAuditEventsQuery(int PageSize, string? PageToken)
    : IRequest<PagedResponse<AuditEventDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.AuditRead;
}

internal sealed class GetRecentAuditEventsQueryValidator : AbstractValidator<GetRecentAuditEventsQuery>
{
    public GetRecentAuditEventsQueryValidator() => RuleFor(q => q.PageSize).PageSize();
}

internal sealed class GetRecentAuditEventsQueryHandler
    : IRequestHandler<GetRecentAuditEventsQuery, PagedResponse<AuditEventDto>>
{
    private readonly IAuditRepository _repo;

    public GetRecentAuditEventsQueryHandler(IAuditRepository repo) => _repo = repo;

    public async Task<PagedResponse<AuditEventDto>> Handle(
        GetRecentAuditEventsQuery request,
        CancellationToken cancellationToken)
    {
        var offset = OffsetPageToken.Parse(request.PageToken);
        var (items, hasMore) = await _repo.GetRecentAsync(offset, request.PageSize);

        return new PagedResponse<AuditEventDto>
        {
            Items = items.Select(ToDto).ToList(),
            PageSize = request.PageSize,
            PageToken = hasMore ? OffsetPageToken.Build(offset + request.PageSize) : null,
            HasMore = hasMore
        };
    }

    private static AuditEventDto ToDto(AuditEvent e) =>
        new(e.Id, e.EventType, e.ObjectId, e.ObjectDn, e.Actor, e.Action, e.Success, e.Details, e.Timestamp);
}
