using MediatR;
using Nelsar.Axia.Application.Features.RateLimitSettings.Commands;
using Nelsar.Axia.Application.Features.RateLimitSettings.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Application.Modules;

/// <summary>The rate limit settings module.</summary>
internal sealed class RateLimitSettingsModule : IRateLimitSettingsModule
{
    private readonly ISender _sender;

    public RateLimitSettingsModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<IEnumerable<RateLimitSettingsDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _sender.Send(new GetAllRateLimitSettingsQuery(), cancellationToken);

    public Task<RateLimitSettingsDto?> GetByPolicyNameAsync(
        string policyName,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new GetRateLimitSettingsByPolicyQuery(policyName), cancellationToken);

    public Task<RateLimitSettingsDto> UpsertAsync(
        string policyName,
        RateLimitSettingsUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new UpsertRateLimitSettingsCommand(
                policyName,
                request.PermitLimit,
                request.WindowSeconds,
                request.QueueLimit,
                request.Enabled),
            cancellationToken);
}
