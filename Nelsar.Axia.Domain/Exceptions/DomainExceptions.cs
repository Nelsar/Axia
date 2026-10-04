namespace Nelsar.Axia.Domain.Exceptions;

/// <summary>
/// Base for failures caused by the caller's request rather than by the system.
///
/// The distinction matters at the edge: these carry a message written for the client and a
/// status code that says "fix your request", whereas everything else is a bug or an outage
/// and must surface as a 500 with no internal detail leaked.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}

/// <summary>
/// A value breaks a domain rule — a malformed address, a blank identifier. The request is
/// not well formed, so it maps to HTTP 400.
/// </summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The request is well formed but collides with existing state — creating a user that is
/// already in the directory. Maps to HTTP 409.
/// </summary>
public sealed class DomainConflictException : DomainException
{
    public DomainConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// The object the request names does not exist. Maps to HTTP 404.
///
/// Distinct from <see cref="DomainConflictException"/> on purpose: "you asked for something
/// that isn't there" and "what you asked for clashes with what is there" are opposite
/// situations, and collapsing both into 409 left a client unable to tell them apart.
/// </summary>
public sealed class DomainNotFoundException : DomainException
{
    public DomainNotFoundException(string message) : base(message)
    {
    }
}
