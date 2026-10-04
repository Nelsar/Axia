using System;

namespace Nelsar.Axia.Contracts.Common;

public record RateLimitSettingsDto
(
    Guid Id,
    string PolicyName,
    int PermitLimit,
    int WindowSeconds,
    int QueueLimit,
    bool Enabled
);
