namespace Nelsar.Axia.Application.Common.Authorization;

/// <summary>
/// Permissions a use case demands on top of its <see cref="IRequirePermission.RequiredPermission"/>,
/// worked out from what the request actually asks for.
///
/// <see cref="IRequirePermission"/> names one permission per request type, which is enough when
/// the type alone says what will happen. A bulk batch is the case where it is not: the same
/// command creates accounts on one row and rewrites another account's password on the next. Its
/// single requirement, users.bulk, said only that the caller may submit batches, and that was
/// the whole check — so a batch could do what neither users.create nor users.update would have
/// allowed on their own.
///
/// Every permission listed here must be held as well as the required one; the authorisation
/// behaviour refuses the request otherwise, before any of it runs.
/// </summary>
public interface IRequireAdditionalPermissions : IRequirePermission
{
    /// <summary>Each one of <see cref="Domain.Authorization.Permissions"/>.</summary>
    IEnumerable<string> AdditionalPermissions { get; }
}
