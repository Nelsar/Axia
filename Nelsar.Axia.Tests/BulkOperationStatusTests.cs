using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Tests;

public class BulkOperationStatusTests
{
    [Fact]
    public void WireValues_AreExactlyWhatClientsCompareAgainst()
    {
        // Pinned deliberately. Extracting the literals into constants made them easy to rename,
        // and a rename here is a silent breaking change: frontend/js/pages/users.js decides
        // between the "ок" and "ошибка" badges by comparing these strings.
        Assert.Equal("completed", BulkOperationStatus.Completed);
        Assert.Equal("failed", BulkOperationStatus.Failed);
    }

    [Fact]
    public void AnItemThatNeverConcluded_DefaultsToFailed()
    {
        // Defaulting to success would report an item as created when the code path that was
        // supposed to set the status threw before reaching it.
        Assert.Equal(BulkOperationStatus.Failed, new BulkUserResult().Status);
    }
}
