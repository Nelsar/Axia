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
    /// otherwise have them travel through this API to anyone allowed to view a user.
    ///
    /// Names alone used to be the whole rule, and the list missed the one that mattered most in
    /// practice: LAPS, which keeps a computer's local administrator password in clear text on the
    /// entry (ms-Mcs-AdmPwd, msLAPS-Password). A name list is only as good as the last time
    /// someone remembered a schema, so it is now backed by <see cref="SecretMarkers"/>: anything
    /// whose name says password, hash, secret or credential is withheld too. That over-reaches
    /// now and then — pwdLastSet is a date — and withholding a date is the cheap mistake.
    /// </summary>
    private static readonly HashSet<string> SecretAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "authPassword",
        "unixUserPassword",
        "msSFU30Password",
        "ms-Mcs-AdmPwd",
        "msLAPS-Password",
        "msLAPS-EncryptedPassword",
        "msLAPS-EncryptedPasswordHistory",
        "msLAPS-EncryptedDSRMPassword",
        "msLAPS-EncryptedDSRMPasswordHistory",
        "msFVE-RecoveryPassword",
        "msTPM-OwnerInformation",
        "msTPM-TpmInformationForComputer",
        "msPKIAccountCredentials",
        "msPKIDPAPIMasterKeys",
        "ipaNTHash",
        "krbPwdHistory",
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

    /// <summary>Substrings that mark an attribute as a secret whatever schema it comes from.</summary>
    private static readonly string[] SecretMarkers = ["password", "pwd", "secret", "credential", "hash"];

    /// <summary>Every attribute LAPS defines is withheld, whether or not it is a password itself.</summary>
    private const string LapsPrefix = "msLAPS-";

    internal static bool IsSecret(string name) =>
        SecretAttributes.Contains(name)
        || name.StartsWith(LapsPrefix, StringComparison.OrdinalIgnoreCase)
        || SecretMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));

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

        if (IsSecret(name))
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
