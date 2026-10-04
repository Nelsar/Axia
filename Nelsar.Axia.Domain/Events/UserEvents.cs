namespace Nelsar.Axia.Domain.Events;

/// <summary>A user proved their identity against the directory.</summary>
public sealed record UserAuthenticationSucceeded(string Actor, string? Dn) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>An authentication attempt was rejected. <paramref name="Reason"/> is for operators, never for the caller.</summary>
public sealed record UserAuthenticationFailed(string Actor, string? Reason) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A user account was created in the directory.</summary>
public sealed record UserCreated(Guid UserId, string Actor) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>An existing user account was modified.</summary>
public sealed record UserUpdated(Guid UserId, string Actor) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
