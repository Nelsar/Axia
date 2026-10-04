namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// One entry in the audit trail: who did what, to which directory object, and whether it worked.
///
/// Built through the two factories rather than by assignment, for a reason specific to auditing:
/// this record is written on the failure path of other operations, where an exception thrown by
/// the write itself loses the very event worth keeping. So the over-long values that would have
/// failed at SaveChanges — a details string longer than its column, a DN from a deep tree — are
/// truncated here instead of rejected. A shortened audit entry is evidence; a lost one is not.
///
/// <see cref="Actor"/> is personal data by definition, and unlike a log line it is meant to be:
/// naming who acted is the entry's whole purpose. That is why it is kept here and masked in logs
/// rather than the other way round.
/// </summary>
public class AuditEvent
{
    public const string AuthenticationEventType = "Authentication";
    public const string ModificationEventType = "Modification";

    public const int MaximumEventTypeLength = 128;
    public const int MaximumActorLength = 256;
    public const int MaximumActionLength = 256;
    public const int MaximumObjectDnLength = 1024;
    public const int MaximumDetailsLength = 4000;

    /// <summary>When the actor could not be established. Never blank — a row that names nobody
    /// is worse than one that says so.</summary>
    private const string UnknownActor = "(unknown)";

    /// <summary>For EF materialisation only.</summary>
    private AuditEvent()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>One of <see cref="AuthenticationEventType"/>, <see cref="ModificationEventType"/>.</summary>
    public string EventType { get; private set; } = string.Empty;

    public Guid? ObjectId { get; private set; }
    public string? ObjectDn { get; private set; }

    /// <summary>Who performed the action.</summary>
    public string Actor { get; private set; } = string.Empty;

    /// <summary>What was done.</summary>
    public string Action { get; private set; } = string.Empty;

    public bool Success { get; private set; }
    public string? Details { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>A sign-in attempt, successful or not.</summary>
    public static AuditEvent Authentication(
        string? actor,
        bool success,
        Guid? objectId,
        string? objectDn,
        string? details,
        DateTimeOffset occurredAt) =>
        Record(AuthenticationEventType, objectId, objectDn, actor, "Authenticate", success, details, occurredAt);

    /// <summary>A change to a directory object, attempted or completed.</summary>
    public static AuditEvent Modification(
        Guid? objectId,
        string? objectDn,
        string? actor,
        string? action,
        bool success,
        string? details,
        DateTimeOffset occurredAt) =>
        Record(ModificationEventType, objectId, objectDn, actor, action, success, details, occurredAt);

    private static AuditEvent Record(
        string eventType,
        Guid? objectId,
        string? objectDn,
        string? actor,
        string? action,
        bool success,
        string? details,
        DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = Truncate(eventType, MaximumEventTypeLength)!,
            ObjectId = objectId,
            ObjectDn = Truncate(objectDn, MaximumObjectDnLength),
            Actor = string.IsNullOrWhiteSpace(actor) ? UnknownActor : Truncate(actor, MaximumActorLength)!,
            Action = Truncate(action, MaximumActionLength) ?? string.Empty,
            Success = success,
            Details = Truncate(details, MaximumDetailsLength),
            Timestamp = occurredAt
        };

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
