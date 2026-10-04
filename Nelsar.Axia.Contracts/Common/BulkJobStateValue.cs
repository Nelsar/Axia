using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Common;

/// <summary>
/// The wire values carried by <see cref="BulkOperationResponse{T}.State"/>.
///
/// These used to be produced with <c>job.State.ToString().ToLowerInvariant()</c>, which quietly
/// made the names of <c>BulkJobState</c> members part of the public API: renaming
/// <c>InProgress</c> — an ordinary, safe-looking refactor that no compiler or test objected to —
/// would have changed what clients receive. frontend/js/pages/users.js both compares against
/// these strings and prints them, so the break would have been silent on both counts.
///
/// Only the values live here, because only the values are the contract. The job state they are
/// projected from is an Application concern, and mapping it is
/// <c>Nelsar.Axia.Application.Services.BulkJobStateWire</c>'s job — keeping the enum out of this
/// assembly is what stops its member names from becoming public API all over again.
/// </summary>
public static class BulkJobStateValue
{
    public const string Pending = "pending";

    /// <summary>Deliberately unseparated: this is what ToString().ToLowerInvariant() produced,
    /// and changing it now would be the very break this class exists to prevent.</summary>
    public const string InProgress = "inprogress";

    public const string Completed = "completed";
    public const string Failed = "failed";
}
