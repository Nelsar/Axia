namespace Nelsar.Axia.Contracts.Common;

/// <summary>
/// A directory a caller may sign in to, as far as an unauthenticated caller is told: what to
/// call it, and what to put in the X-LDAP-Provider-Id header.
///
/// Deliberately not <see cref="ProviderDto"/>, and deliberately not a subset produced by nulling
/// fields out. <see cref="ProviderDto"/> carries the host, the failover hosts, the port, the base
/// DN, the bind account and the search filter — a description of where the directory lives and
/// how it is reached, which is a map for anyone who wants to attack it directly and none of which
/// a sign-in page needs. A separate type means the two projections cannot drift into each other:
/// a field added to the administrative DTO tomorrow does not silently appear on the anonymous one.
///
/// What it does disclose is real and worth stating plainly: the names and ids of the directories
/// this deployment serves. That is the minimum a person needs to answer "which of these am I in?"
/// before they have any credentials accepted, and it is the same trade every multi-tenant sign-in
/// page makes. An operator who cannot afford even that should keep the endpoint off the public
/// edge rather than expect the payload to hide it.
/// </summary>
/// <param name="Id">Goes in the X-LDAP-Provider-Id header, verbatim.</param>
/// <param name="Name">The administrator-chosen label, for a person to pick from.</param>
public record SelectableProviderDto(Guid Id, string Name);
