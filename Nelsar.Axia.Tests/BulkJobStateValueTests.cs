using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Tests;

public class BulkJobStateValueTests
{
    [Theory]
    [InlineData(BulkJobState.Pending, "pending")]
    // Unseparated on purpose — this is what ToString().ToLowerInvariant() used to emit, and it
    // is what clients already receive.
    [InlineData(BulkJobState.InProgress, "inprogress")]
    [InlineData(BulkJobState.Completed, "completed")]
    [InlineData(BulkJobState.Failed, "failed")]
    public void EachState_KeepsTheValueClientsAlreadyReceive(BulkJobState state, string expected)
    {
        // frontend/js/pages/users.js stops polling on "completed"/"failed" and prints the value
        // verbatim, so a change here is a breaking change no compiler would have flagged.
        Assert.Equal(expected, BulkJobStateWire.For(state));
    }

    [Fact]
    public void EveryStateHasAValue_IncludingOnesAddedLater()
    {
        // The reason this class exists. Adding a member to BulkJobState without giving it a wire
        // value used to produce one silently from the member's name; now it throws, and this
        // test is what surfaces it before it ships.
        foreach (var state in Enum.GetValues<BulkJobState>())
        {
            var value = BulkJobStateWire.For(state);

            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Equal(value.ToLowerInvariant(), value);
        }
    }

    [Fact]
    public void AnUnmappedState_ThrowsRatherThanInventingAValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BulkJobStateWire.For((BulkJobState)999));
    }

    [Fact]
    public void AResponseThatWasNeverPopulated_ReadsAsPending()
    {
        Assert.Equal(BulkJobStateValue.Pending, new BulkOperationResponse<BulkUserResult>().State);
    }

    [Fact]
    public void RenamingAnEnumMember_NoLongerChangesTheContract()
    {
        // Pins the decoupling itself: the wire value is no longer derived from the member name.
        // If someone reverts For() to ToString(), InProgress would still pass — but a later
        // rename to "Running" would then break clients, which is exactly what this guards.
        Assert.NotEqual(nameof(BulkJobState.InProgress), BulkJobStateWire.For(BulkJobState.InProgress));
    }
}
