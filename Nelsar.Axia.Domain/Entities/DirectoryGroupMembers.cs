using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Who a group lists as its members, in the forms directories keep them: distinguished names
/// (<c>member</c>, <c>uniqueMember</c>) and, for a posixGroup, logins (<c>memberUid</c>).
/// </summary>
public sealed record DirectoryGroupMembers(IReadOnlyList<string> Dns, IReadOnlyList<string> Logins)
{
    /// <summary>A group that is not there, or lists nobody.</summary>
    public static DirectoryGroupMembers None { get; } = new([], []);

    /// <summary>
    /// Whether the group lists this entry — by its DN, compared as a distinguished name so that
    /// case and spacing do not matter, or by its login.
    /// </summary>
    public bool Includes(LdapUserInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrWhiteSpace(entry.Username)
            && Logins.Contains(entry.Username, StringComparer.OrdinalIgnoreCase))
            return true;

        if (!DistinguishedName.TryCreate(entry.Dn, out var own))
            return false;

        return Dns.Any(member =>
            DistinguishedName.TryCreate(member, out var listed)
            && string.Equals(listed!.Normalized, own!.Normalized, StringComparison.OrdinalIgnoreCase));
    }
}
