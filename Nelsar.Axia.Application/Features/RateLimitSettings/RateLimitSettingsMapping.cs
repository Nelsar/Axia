using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Application.Features.RateLimitSettings;

internal static class RateLimitSettingsMapping
{
    public static RateLimitSettingsDto ToDto(Domain.Entities.RateLimitSettings s) =>
        new(s.Id, s.PolicyName, s.PermitLimit, s.WindowSeconds, s.QueueLimit, s.Enabled);
}
