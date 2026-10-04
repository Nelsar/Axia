namespace Nelsar.Axia.Contracts.Exceptions;

/// <summary>
/// The directory behind <c>ILdapService</c> could not service the operation — it was
/// unreachable, it rejected the request, or the endpoint we are pointed at cannot perform
/// this kind of operation at all (a Global Catalog port is read-only, for example).
///
/// Deliberately not a <c>DomainException</c>: nothing about the caller's request was wrong,
/// so it maps to HTTP 502 rather than to a 4xx. Telling a client "fix your request" when an
/// upstream server is down sends them chasing a problem they cannot solve, and hides an
/// outage that should be alerting somebody.
/// </summary>
public sealed class DirectoryUnavailableException : Exception
{
    public DirectoryUnavailableException(string message) : base(message)
    {
    }

    public DirectoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
