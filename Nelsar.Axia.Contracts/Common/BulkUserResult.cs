namespace Nelsar.Axia.Contracts.Common;

public class BulkUserResult
{
    public string Email { get; set; } = string.Empty;
    /// <summary>One of <see cref="BulkOperationStatus"/>. Defaults to failed so an item
    /// that never reached a conclusion is never reported as a success.</summary>
    public string Status { get; set; } = BulkOperationStatus.Failed;
    public string Message { get; set; } = string.Empty;
    public UserDto? User { get; set; }
}
