using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Contracts.Responses;

public class UserListingResponse
{
    public IReadOnlyList<UserDto> Items { get; set; } = Array.Empty<UserDto>();

    /// <summary>
    /// False when the directory enumeration was cut short by a failure. The items present are
    /// genuine, but the list is not the whole directory — a client must not present it as one,
    /// and must not conclude that a missing user does not exist.
    /// </summary>
    public bool IsComplete { get; set; } = true;
}
