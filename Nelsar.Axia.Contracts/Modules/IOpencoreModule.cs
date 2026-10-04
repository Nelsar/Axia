namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// The root of the module surface: what a host may ask of this system, stated in nothing but
/// wire types.
///
/// A host — the HTTP API today, a gRPC front end or an in-process consumer tomorrow — talks to
/// a module and to nothing else. It does not construct commands, it does not know that MediatR
/// dispatches them, and it does not reference the Application assembly to say what it wants.
/// Everything on a module's methods comes from this project, which depends on nothing, so the
/// contract is publishable on its own.
///
/// This interface itself carries no members. It is the marker that says "an entry point into
/// the system", so registration and boundary tests can find the whole set by type rather than
/// by a list that goes stale. The operations live on the per-feature interfaces that derive
/// from it — <see cref="IUsersModule"/>, <see cref="IGroupsModule"/>, and the rest — so a
/// caller depends on the slice it uses instead of on all forty-odd use cases at once.
/// </summary>
public interface IOpencoreModule;
