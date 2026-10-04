using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Common;

/// <summary>
/// The per-item outcome values carried by <see cref="BulkUserResult.Status"/>.
///
/// These are part of the JSON contract, not display text: the frontend switches on them
/// (<c>i.status === "completed"</c> in frontend/js/pages/users.js), so they must stay exactly
/// as they are — never localised, never re-cased. The constants exist so that the value is
/// written once instead of eight times; a typo in one of those eight silently miscounted
/// <see cref="BulkOperationResponse{T}.CompletedCount"/> with nothing to catch it.
/// </summary>
public static class BulkOperationStatus
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}
