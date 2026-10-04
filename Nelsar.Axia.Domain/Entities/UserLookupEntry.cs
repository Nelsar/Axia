using Nelsar.Axia.Domain.Common;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Maps a derived user id back to the directory address it was derived from.
///
/// <c>UserDto.Id</c> is SHA-256 of the address (see DeterministicIdFactory), so it cannot be
/// inverted and the directory has no attribute to search it by. Without this index the only
/// way to answer "who is id X?" is to enumerate every entry and recompute each id — O(n) over
/// the whole directory, on every request that misses the in-process cache.
///
/// The row is a hint, never the truth: the directory is still consulted on every resolution,
/// and a row that no longer maps back to the same id is discarded as stale.
/// </summary>
public class UserLookupEntry
{
    /// <summary>RFC 3696 maximum — 64-char local part, '@', 255-char domain.</summary>
    public const int MaximumEmailLength = 320;

    /// <summary>For EF materialisation only.</summary>
    private UserLookupEntry()
    {
    }

    /// <summary>The derived id — always equal to DeterministicIdFactory.BuildUserId(Email).</summary>
    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>When this identity was last observed, so abandoned rows can be pruned.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    public static UserLookupEntry Remember(Guid id, string? email, DateTimeOffset seenAt) =>
        new()
        {
            Id = id,
            Email = DomainGuard.RequiredText(email, nameof(Email), MaximumEmailLength),
            LastSeenAt = seenAt
        };

    /// <summary>
    /// Records that this identity was seen again, under whatever address resolved it this time.
    /// The address can legitimately change — an account's mail attribute is edited, or it is
    /// reached by uid on one request and by mail on the next — and the row follows it rather
    /// than being left to describe an address that no longer resolves.
    /// </summary>
    public void SeenAgain(string? email, DateTimeOffset seenAt)
    {
        Email = DomainGuard.RequiredText(email, nameof(Email), MaximumEmailLength);
        LastSeenAt = seenAt;
    }
}
