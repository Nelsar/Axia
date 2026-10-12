namespace Nelsar.Axia.Contracts.Exceptions;

/// <summary>
/// The work was refused because the API already holds as much of it as it is willing to, not
/// because anything about the request was wrong. Maps to HTTP 503 with a Retry-After header: the
/// same request, a little later, will be accepted.
/// </summary>
public sealed class ServiceBusyException : Exception
{
    public ServiceBusyException(string message, TimeSpan retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>How long the caller should wait before trying again.</summary>
    public TimeSpan RetryAfter { get; }
}
