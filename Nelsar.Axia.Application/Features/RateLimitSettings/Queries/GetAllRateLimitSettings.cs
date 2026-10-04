using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RateLimitSettings.Queries;

internal sealed record GetAllRateLimitSettingsQuery : IRequest<IEnumerable<RateLimitSettingsDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RateLimitsManage;
}

internal sealed class GetAllRateLimitSettingsQueryHandler
    : IRequestHandler<GetAllRateLimitSettingsQuery, IEnumerable<RateLimitSettingsDto>>
{
    private readonly IRateLimitSettingsRepository _repo;

    public GetAllRateLimitSettingsQueryHandler(IRateLimitSettingsRepository repo) => _repo = repo;

    public async Task<IEnumerable<RateLimitSettingsDto>> Handle(
        GetAllRateLimitSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var list = await _repo.GetAllAsync();
        return list.Select(RateLimitSettingsMapping.ToDto);
    }
}
