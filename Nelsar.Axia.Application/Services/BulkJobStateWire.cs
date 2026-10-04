using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Application.Services;

/// <summary>
/// Projects <see cref="BulkJobState"/> onto the wire values in <see cref="BulkJobStateValue"/>.
///
/// The mapping is explicit and total. A new enum member with no value here throws rather than
/// inventing one, and <c>BulkJobStateValueTests</c> catches that at test time.
/// </summary>
internal static class BulkJobStateWire
{
    public static string For(BulkJobState state) => state switch
    {
        BulkJobState.Pending => BulkJobStateValue.Pending,
        BulkJobState.InProgress => BulkJobStateValue.InProgress,
        BulkJobState.Completed => BulkJobStateValue.Completed,
        BulkJobState.Failed => BulkJobStateValue.Failed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(state), state, "No wire value is defined for this bulk job state.")
    };
}
