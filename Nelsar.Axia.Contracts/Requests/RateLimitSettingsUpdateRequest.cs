using System.ComponentModel.DataAnnotations;

namespace Nelsar.Axia.Contracts.Requests;

public class RateLimitSettingsUpdateRequest
{
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; set; } = 60;

    [Range(0, int.MaxValue)]
    public int QueueLimit { get; set; }

    public bool Enabled { get; set; } = true;
}
