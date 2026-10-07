using System.Text;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// How a directory is told a new password — the two ways that let the directory itself hash and
/// enforce it, and nothing else.
///
/// Passwords used to be written as a plain <c>userPassword</c> attribute. In Active Directory that
/// is an ordinary string attribute (unless dSHeuristics says otherwise, which by default it does
/// not): the account's real password was left unchanged, and the new one was stored in clear text
/// on the entry for anyone allowed to read it. In OpenLDAP without a hashing policy the value
/// stored is the value written, so every password set through this application sat in the
/// directory unhashed.
/// </summary>
internal static class DirectoryPasswords
{
    /// <summary>RFC 3062, LDAP Password Modify. The server hashes the value per its own policy.</summary>
    public const string PasswordModifyOid = "1.3.6.1.4.1.4203.1.11.1";

    /// <summary>LDAP_CAP_ACTIVE_DIRECTORY_OID, in the root DSE's supportedCapabilities.</summary>
    public const string ActiveDirectoryCapability = "1.2.840.113556.1.4.800";

    /// <summary>
    /// The unicodePwd value Active Directory expects: the password in double quotes, UTF-16LE.
    /// AD accepts it only over an encrypted connection and checks it against the domain policy.
    /// </summary>
    public static byte[] ActiveDirectoryValue(string password) =>
        Encoding.Unicode.GetBytes($"\"{password}\"");

    /// <summary>
    /// The BER-encoded PasswdModifyRequestValue for setting <paramref name="newPassword"/> on the
    /// entry at <paramref name="userDn"/> — an administrative reset, so no old password:
    /// <code>SEQUENCE { userIdentity [0] OCTET STRING, newPasswd [2] OCTET STRING }</code>
    /// </summary>
    public static byte[] PasswordModifyRequest(string userDn, string newPassword)
    {
        var identity = Encode(0x80, Encoding.UTF8.GetBytes(userDn));
        var password = Encode(0x82, Encoding.UTF8.GetBytes(newPassword));
        return Encode(0x30, [.. identity, .. password]);
    }

    private static byte[] Encode(byte tag, byte[] content) => [tag, .. Length(content.Length), .. content];

    /// <summary>BER definite length: one byte below 128, otherwise 0x80|n followed by n bytes.</summary>
    private static byte[] Length(int length)
    {
        if (length < 0x80)
            return [(byte)length];

        var bytes = new List<byte>();
        for (var remaining = length; remaining > 0; remaining >>= 8)
            bytes.Insert(0, (byte)(remaining & 0xFF));

        return [(byte)(0x80 | bytes.Count), .. bytes];
    }
}
