namespace Nelsar.Axia.Contracts.Exceptions;

/// <summary>
/// The endpoint is part of the API contract but the capability behind it has not been built.
/// Maps to HTTP 501.
///
/// Distinct from a 404 on purpose: 404 says "there is no such thing", which sends a client
/// looking for a typo in their URL. 501 says "this route is real, the feature is not yet" —
/// the honest answer while a planned capability is still outstanding.
///
/// Raised by the service rather than short-circuited in the controller, so that the fact lives
/// in exactly one place: implementing the service method is all it takes for the endpoint to
/// start working.
/// </summary>
public sealed class FeatureNotImplementedException : Exception
{
    public FeatureNotImplementedException(string message) : base(message)
    {
    }
}
