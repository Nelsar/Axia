using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Application.Services;

/// <summary>
/// The principal when the host has supplied none — nobody, and not a system context.
///
/// Registered by <c>AddApplicationLayer</c> through TryAdd, so a real implementation always wins.
/// It exists for the case where one was never registered: a new front end, a test harness, a
/// worker someone stood up quickly. Reporting "no principal" makes every guarded use case answer
/// 401 there, which is loud and safe. Reporting <see cref="ICurrentUser.IsSystem"/> would have
/// been the other obvious default and is the dangerous one — a forgotten registration would then
/// run every guarded operation as though the application itself had asked for it.
/// </summary>
internal sealed class AnonymousCurrentUser : ICurrentUser
{
    public string? Subject => null;

    public string? DistinguishedName => null;

    public Guid? ProviderId => null;

    public bool IsSystem => false;
}
