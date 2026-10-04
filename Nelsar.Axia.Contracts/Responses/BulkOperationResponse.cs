using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Contracts.Responses;

public class BulkOperationResponse<T>
{
    public string JobId { get; set; } = string.Empty;
    /// <summary>One of <see cref="BulkJobStateValue"/>.</summary>
    public string State { get; set; } = BulkJobStateValue.Pending;
    public IEnumerable<T> Items { get; set; } = Array.Empty<T>();
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public string? ErrorMessage { get; set; }
}
