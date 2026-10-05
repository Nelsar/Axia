using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users;

/// <summary>What one row of a bulk batch asks for.</summary>
internal enum BulkUserOperation
{
    Create,
    Update,

    /// <summary>
    /// Anything else. It used to be read as Create — every value that was not "update" was —
    /// so a sheet asking to "delete" a row created the account instead. It now fails as its own
    /// row and needs no permission, because it does nothing.
    /// </summary>
    Unknown
}

internal static class BulkUserOperations
{
    public static BulkUserOperation Of(BulkUserRequest request)
    {
        var operation = request.Operation?.Trim();

        // Blank keeps meaning create, which is the request's documented default.
        if (string.IsNullOrEmpty(operation) || operation.Equals("create", StringComparison.OrdinalIgnoreCase))
            return BulkUserOperation.Create;

        if (operation.Equals("update", StringComparison.OrdinalIgnoreCase))
            return BulkUserOperation.Update;

        return BulkUserOperation.Unknown;
    }

    /// <summary>
    /// The permissions a batch needs on top of users.bulk: the single-account permission for
    /// each kind of row it contains. A batch is an efficient way to do what the caller could do
    /// one account at a time, never a way to do more.
    /// </summary>
    public static IEnumerable<string> PermissionsFor(IEnumerable<BulkUserRequest>? requests) =>
        (requests ?? [])
            .OfType<BulkUserRequest>()
            .Select(Of)
            .Distinct()
            .Select(operation => operation switch
            {
                BulkUserOperation.Create => Permissions.UsersCreate,
                BulkUserOperation.Update => Permissions.UsersUpdate,
                _ => null
            })
            .OfType<string>();
}
