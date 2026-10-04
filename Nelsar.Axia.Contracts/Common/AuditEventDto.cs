namespace Nelsar.Axia.Contracts.Common;

public record AuditEventDto(
    Guid Id,
    string EventType,
    Guid? ObjectId,
    string? ObjectDn,
    string Actor,
    string Action,
    bool Success,
    string? Details,
    DateTimeOffset Timestamp);
