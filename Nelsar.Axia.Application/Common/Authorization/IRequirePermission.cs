using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Common.Authorization;

/// <summary>
/// Declares the permission a use case demands before it will run.
///
/// Authorisation already existed at the edge, as <c>[Authorize(Policy = ...)]</c> on the
/// controller action. That check is real but it guards a <em>route</em>, not an operation: it is
/// absent by construction from every other way a command can be dispatched — a background job, a
/// future gRPC or CLI front end, a handler that sends another command — and it is one forgotten
/// attribute away from being absent from a new endpoint too. Stating the requirement on the
/// request itself moves it to the thing being protected, so it holds however the request arrives
/// and reads at the use case rather than three layers away.
///
/// The controller attributes stay. Two independent checks that must both pass is the point:
/// the edge refuses an unauthorised caller before any work is done, and this refuses one that
/// reached the handler by a path nobody thought about.
/// </summary>
public interface IRequirePermission
{
    /// <summary>One of <see cref="Permissions"/>.</summary>
    string RequiredPermission { get; }
}
