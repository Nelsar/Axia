using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Application.Services;

public enum BulkJobState
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public class BulkUserJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<BulkUserRequest> Requests { get; set; } = new();

    /// <summary>
    /// The directory this batch was queued against, or null for the configured default.
    ///
    /// Recorded at the moment a person asks, because that is the last moment it is knowable: the
    /// worker picks the job up on its own thread, with no request and therefore no
    /// <c>X-LDAP-Provider-Id</c> to read. Without it the batch ran wherever the default pointed —
    /// a hundred accounts created in the wrong domain, reported as a completed job.
    /// </summary>
    public Guid? ProviderId { get; set; }

    /// <summary>
    /// The subject of whoever queued the batch. The worker runs the job as them (see
    /// BackgroundPrincipal), so its rows are authorised against their permissions and audited
    /// under their name.
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// The entry of whoever queued the batch — what the worker resolves their permissions from,
    /// alongside <see cref="RequestedBy"/>, which is what it audits them as.
    /// </summary>
    public string? RequestedByDn { get; set; }

    public List<BulkUserResult> Results { get; set; } = new();
    public BulkJobState State { get; set; } = BulkJobState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
