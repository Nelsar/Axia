namespace Nelsar.Axia.Domain.ValueObjects;

/// <summary>
/// How the connection to a directory is protected.
///
/// It matters more here than for most LDAP clients, because of what crosses this connection:
/// signing in is a simple bind, so the user's password travels to the directory as it was typed,
/// and so does the service account's on every request. Until this existed every connection was
/// plain LDAP, and every one of those passwords could be read by anyone on the network path.
/// </summary>
public enum LdapSecurity
{
    /// <summary>TLS from the first byte, on the LDAPS port (636). The default.</summary>
    Ldaps,

    /// <summary>
    /// Plain LDAP on 389, upgraded with the StartTLS extended operation before anything else is
    /// sent. Fails rather than continuing unencrypted when the directory does not offer it.
    /// </summary>
    StartTls,

    /// <summary>No protection at all. Has to be chosen explicitly.</summary>
    None
}

/// <summary>
/// The ports each <see cref="LdapSecurity"/> mode is used on, and the ones it can never work on.
/// </summary>
public static class LdapSecurityPorts
{
    public const int Ldap = 389;
    public const int Ldaps = 636;
    public const int GlobalCatalog = 3268;
    public const int GlobalCatalogOverTls = 3269;

    /// <summary>The port a mode is served on when nobody says otherwise.</summary>
    public static int StandardPortFor(LdapSecurity security) =>
        security == LdapSecurity.Ldaps ? Ldaps : Ldap;

    /// <summary>
    /// Whether <paramref name="port"/> is one where this mode cannot possibly work: TLS from the
    /// first byte on a port that speaks plain LDAP, or plain LDAP on one that expects a TLS
    /// handshake. Each fails at connect with an error that says nothing about the setting, which
    /// is why these are refused where they are written instead.
    /// </summary>
    public static bool Contradicts(LdapSecurity security, int port) =>
        security == LdapSecurity.Ldaps
            ? port is Ldap or GlobalCatalog
            : port is Ldaps or GlobalCatalogOverTls;

    /// <summary>
    /// Reads a mode by name, ignoring case. Unlike <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// it does not accept a number: "2" would otherwise quietly mean None.
    /// </summary>
    public static bool TryParse(string? value, out LdapSecurity security)
    {
        security = default;
        var name = value?.Trim();

        return !string.IsNullOrEmpty(name)
            && !char.IsAsciiDigit(name[0]) && name[0] is not ('-' or '+')
            && Enum.TryParse(name, ignoreCase: true, out security)
            && Enum.IsDefined(security);
    }
}
