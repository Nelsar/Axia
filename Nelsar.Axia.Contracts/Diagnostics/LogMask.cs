namespace Nelsar.Axia.Contracts.Diagnostics;

/// <summary>
/// Renders identifiers for the log with the personal part removed.
///
/// Logs from an identity service are a standing collection of who signed in, when, from where and
/// how often — personal data by any reading of GDPR Art. 4, retained wherever the log ships to,
/// for however long that system keeps things, and read by people who never needed the names. What
/// operations actually needs from a failed sign-in is that it failed, roughly whose it was, and
/// the shape of the account. That is what these leave behind.
///
/// The audit trail is the deliberate exception: an <c>AuditEvent</c> records the actor in full,
/// because naming who acted is the entire purpose of an audit entry. Masking belongs in the
/// diagnostic log, which nothing depends on for attribution.
///
/// Here rather than in Application because every layer logs, the host included — its request-
/// completion event names the caller — and a masking rule that only some of them can reach is a
/// rule with holes in it.
/// </summary>
public static class LogMask
{
    private const string Absent = "(none)";
    private const string Hidden = "***";

    /// <summary>
    /// A login or email with its identifying middle removed: "ivan.ivanov@example.com" becomes
    /// "i***@example.com", "jsmith" becomes "j***h".
    ///
    /// The domain survives because it names an organisation rather than a person, and it is what
    /// tells an operator which directory a wave of failures is coming from. The first character
    /// survives so that two different accounts failing are still visibly two.
    /// </summary>
    public static string Login(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Absent;

        var login = value.Trim();
        var at = login.LastIndexOf('@');

        if (at > 0)
            return $"{Elide(login[..at])}@{login[(at + 1)..]}";

        return Elide(login);
    }

    /// <summary>
    /// A distinguished name with only its leftmost component masked:
    /// "CN=Ivan Ivanov,OU=Staff,DC=example,DC=com" becomes "CN=I***,OU=Staff,DC=example,DC=com".
    ///
    /// The path is the useful half — which OU, which domain, which tree the operation was in —
    /// and none of it is personal. The RDN is the person's name, and it is the only part removed.
    /// </summary>
    public static string DistinguishedName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Absent;

        var dn = value.Trim();
        var separator = dn.IndexOf(',');
        var rdn = separator < 0 ? dn : dn[..separator];
        var remainder = separator < 0 ? string.Empty : dn[separator..];

        var equals = rdn.IndexOf('=');
        if (equals < 0)
            return Elide(rdn) + remainder;

        return $"{rdn[..(equals + 1)]}{Elide(rdn[(equals + 1)..])}{remainder}";
    }

    private static string Elide(string value)
    {
        var trimmed = value.Trim();

        // Nothing to keep a first character of, and a single character is the whole value.
        if (trimmed.Length <= 1)
            return Hidden;

        return $"{trimmed[0]}{Hidden}";
    }
}
