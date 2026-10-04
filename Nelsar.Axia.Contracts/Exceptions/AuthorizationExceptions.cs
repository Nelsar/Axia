namespace Nelsar.Axia.Contracts.Exceptions;

/// <summary>
/// A use case that requires a permission was reached with no principal to resolve one for.
///
/// Maps to HTTP 401. Deliberately not a <c>DomainException</c>: nothing about the request was
/// malformed, so answering 400 would send the caller rewriting a payload that was fine.
/// </summary>
public sealed class NotAuthenticatedException : Exception
{
    public NotAuthenticatedException(string message) : base(message)
    {
    }
}

/// <summary>
/// The caller is known but does not hold the permission the use case requires. Maps to HTTP 403.
///
/// The message names neither the permission nor the holder. Which permission would have opened a
/// door is a map of the authorisation model, drawn for whoever is probing it; that detail belongs
/// in the log, where the operator who has to answer "why was I refused?" can find it.
/// </summary>
public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string message) : base(message)
    {
    }
}
