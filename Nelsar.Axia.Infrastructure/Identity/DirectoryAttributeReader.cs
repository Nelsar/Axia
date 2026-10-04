using System.Text;
using Novell.Directory.Ldap;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// Turns a directory entry's raw attribute set into the port's own <see cref="DirectoryAttribute"/>
/// list, applying the two rules that make raw attributes safe to hand upwards: secrets are never
/// read out, and bytes that are not text are not pretended to be.
/// </summary>
internal static class DirectoryAttributeReader
{
    /// <summary>
    /// Attributes whose values are credential material. Read access to an entry is not read
    /// access to its password hashes: a directory that answers these to the bind account would
    /// otherwise have them travel through this API to anyone allowed to view a user. Kept as
    /// names rather than a pattern because the naming has no common shape across AD, OpenLDAP
    /// and Samba, and a pattern loose enough to catch them all would also catch ordinary
    /// attributes.
    /// </summary>
    private static readonly HashSet<string> SecretAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "userPassword",
        "unicodePwd",
        "dBCSPwd",
        "ntPwdHistory",
        "lmPwdHistory",
        "supplementalCredentials",
        "msDS-ManagedPassword",
        "msDS-ManagedPasswordPreviously",
        "msDS-ManagedPasswordId",
        "krbPrincipalKey",
        "krbExtraData",
        "sambaNTPassword",
        "sambaLMPassword",
        "sambaPasswordHistory",
        "userPKCS12",
        "pKIEnrollmentAccess",
    };

    // Strict on purpose: the default UTF-8 decoder substitutes U+FFFD for invalid bytes, which
    // would turn an objectSid into a string of replacement characters that reads as data and
    // decodes back to nothing.
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    public static IReadOnlyList<DirectoryAttribute> Read(LdapAttributeSet attributes)
    {
        var result = new List<DirectoryAttribute>(attributes.Count);

        foreach (var attribute in attributes.Values)
        {
            result.Add(Convert(attribute));
        }

        // Sorted so the same entry renders in the same order twice running; the directory
        // promises nothing about attribute order.
        result.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static DirectoryAttribute Convert(LdapAttribute attribute)
    {
        var name = attribute.Name;

        if (SecretAttributes.Contains(name))
            return new DirectoryAttribute(name, [], IsRedacted: true);

        var rawValues = attribute.ByteValueArray;
        if (rawValues is { Length: > 0 } && Array.Exists(rawValues, value => !IsText(value)))
        {
            var encoded = Array.ConvertAll(rawValues, System.Convert.ToBase64String);
            return new DirectoryAttribute(name, encoded, IsBinary: true);
        }

        return new DirectoryAttribute(name, attribute.StringValueArray ?? []);
    }

    private static bool IsText(byte[] value)
    {
        try
        {
            var decoded = StrictUtf8.GetString(value);
            foreach (var character in decoded)
            {
                if (char.IsControl(character) && character is not '\t' and not '\n' and not '\r')
                    return false;
            }
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
