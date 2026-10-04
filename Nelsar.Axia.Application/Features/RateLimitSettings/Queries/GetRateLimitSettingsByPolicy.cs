using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.RateLimitSettings.Queries;

/// <summary>Null when no row has been stored for that policy name.</summary>
internal sealed record GetRateLimitSettingsByPolicyQuery(string PolicyName) : IRequest<RateLimitSettingsDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.RateLimitsManage;
}

internal sealed class GetRateLimitSettingsByPolicyQueryHandler
    : IRequestHandler<GetRateLimitSettingsByPolicyQuery, RateLimitSettingsDto?>
{
    private readonly IRateLimitSettingsRepository _repo;

    public GetRateLimitSettingsByPolicyQueryHandler(IRateLimitSettingsRepository repo) => _repo = repo;

    public async Task<RateLimitSettingsDto?> Handle(
        GetRateLimitSettingsByPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var settings = await _repo.GetByPolicyNameAsync(request.PolicyName);
        return settings == null ? null : RateLimitSettingsMapping.ToDto(settings);
    }
}
