using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// The accounts and groups Active Directory protects, by security identifier: the list
/// AdminSDHolder covers, which is what a directory administrator means by "privileged".
///
/// adminCount=1 used to be the only sign read, and it is a mark something else has to make. In
/// Windows that is SDProp, which runs once an hour, so an account added to Domain Admins went
/// unprotected here until it next ran. Samba has no SDProp at all
/// (https://bugzilla.samba.org/show_bug.cgi?id=13954): an administrator made after the domain was
/// provisioned is never marked, and anyone allowed to reset passwords could reset theirs. A SID
/// says what the entry is, or — through tokenGroups or a membership chain — what it belongs to,
/// at the moment it is asked.
/// </summary>
internal static class ProtectedSecurityIdentifiers
{
    /// <summary>
    /// BUILTIN (S-1-5-32-x): Administrators, Account Operators, Server Operators, Print
    /// Operators, Backup Operators, Replicator.
    /// </summary>
    private static readonly uint[] BuiltinRids = [544, 548, 549, 550, 551, 552];

    /// <summary>
    /// Relative to a domain's SID: Domain Admins, Domain Controllers, Schema Admins, Enterprise
    /// Admins, Read-only Domain Controllers, Key Admins, Enterprise Key Admins.
    /// </summary>
    private static readonly uint[] DomainGroupRids = [512, 516, 518, 519, 521, 526, 527];

    /// <summary>The built-in Administrator and krbtgt: accounts, not groups.</summary>
    private static readonly uint[] DomainAccountRids = [500, 502];

    /// <summary>The primary groups that make an account privileged without a memberOf to show for it.</summary>
    internal static readonly uint[] PrivilegedPrimaryGroupRids = [512, 518, 519];

    /// <summary>Whether a binary SID is one of the protected ones.</summary>
    public static bool IsProtected(byte[]? sid)
    {
        if (!TryRead(sid, out var authority, out var subAuthorities))
            return false;

        if (authority != 5 || subAuthorities.Length == 0)
            return false;

        var isBuiltinProtected = subAuthorities is [32, var builtin] && BuiltinRids.Contains(builtin);

        var isDomainSid = subAuthorities[0] == 21 && subAuthorities.Length == 5;
        var domainRid = subAuthorities[^1];
        var isDomainProtected = isDomainSid
            && (DomainGroupRids.Contains(domainRid) || DomainAccountRids.Contains(domainRid));

        return isBuiltinProtected || isDomainProtected;
    }

    /// <summary>
    /// An LDAP filter matching the protected groups of the domain whose SID this is, and the
    /// built-in ones: what to resolve to DNs before asking who is a member of any of them.
    /// </summary>
    public static string GroupFilter(byte[] domainSid)
    {
        if (!TryRead(domainSid, out var authority, out var subAuthorities) || authority != 5
            || subAuthorities is not [21, _, _, _])
        {
            throw new ArgumentException("Not a domain SID.", nameof(domainSid));
        }

        var filter = new StringBuilder("(|");
        foreach (var rid in BuiltinRids)
            filter.Append("(objectSid=").Append(Escape(Build(5, [32, rid]))).Append(')');
        foreach (var rid in DomainGroupRids)
            filter.Append("(objectSid=").Append(Escape(Build(5, [.. subAuthorities, rid]))).Append(')');
        return filter.Append(')').ToString();
    }

    /// <summary>
    /// Revision 1, a count of sub-authorities, a 48-bit big-endian identifier authority, then the
    /// sub-authorities, 32-bit little-endian. MS-DTYP 2.4.2.2.
    /// </summary>
    private static bool TryRead(byte[]? sid, out ulong authority, out uint[] subAuthorities)
    {
        authority = 0;
        subAuthorities = [];

        if (sid is null || sid.Length < 8 || sid[0] != 1 || sid.Length != 8 + 4 * sid[1])
            return false;

        for (var i = 2; i < 8; i++)
            authority = (authority << 8) | sid[i];

        subAuthorities = new uint[sid[1]];
        for (var i = 0; i < subAuthorities.Length; i++)
            subAuthorities[i] = BinaryPrimitives.ReadUInt32LittleEndian(sid.AsSpan(8 + 4 * i, 4));

        return true;
    }

    private static byte[] Build(ulong authority, uint[] subAuthorities)
    {
        var sid = new byte[8 + 4 * subAuthorities.Length];
        sid[0] = 1;
        sid[1] = (byte)subAuthorities.Length;
        for (var i = 0; i < 6; i++)
            sid[2 + i] = (byte)(authority >> (8 * (5 - i)));
        for (var i = 0; i < subAuthorities.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(8 + 4 * i, 4), subAuthorities[i]);
        return sid;
    }

    /// <summary>A binary value written into a filter as \xx escapes, the only form every directory reads.</summary>
    private static string Escape(byte[] value) => string.Concat(value.Select(b => "\\" + b.ToString("x2", CultureInfo.InvariantCulture)));
}
