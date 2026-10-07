using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Configuration;

public class LdapSettings
{
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// The port to connect to. Leave it unset to use the standard one for <see cref="Security"/>:
    /// 636 for LDAPS, 389 otherwise. Read the port to use through <see cref="ResolvedPort"/>.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// How the connection is protected: <c>Ldaps</c> (the default), <c>StartTls</c> or <c>None</c>.
    ///
    /// The default used to be — implicitly, since there was no setting — plain LDAP, and with it
    /// every user's password on its way to the directory, readable on the wire. An instance that
    /// really is meant to speak plain LDAP now has to say <c>None</c>.
    /// </summary>
    public LdapSecurity Security { get; set; } = LdapSecurity.Ldaps;

    /// <summary>
    /// A PEM or DER file with the certificate authority — or, for a self-signed directory, the
    /// certificate itself — to trust when verifying the directory's TLS certificate, in addition
    /// to the operating system's trust store. For a container image that does not carry an
    /// internal CA. The host name is checked against the certificate either way; there is no
    /// setting that turns verification off.
    /// </summary>
    public string? TrustedCaCertificatePath { get; set; }

    public string BaseDn { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    /// <summary>
    /// Matches AD (sAMAccountName/userPrincipalName), OpenLDAP/eDirectory (uid), or email
    /// login in a single query, so the default works against any of the three out of the box.
    /// </summary>
    public string UserSearchFilter { get; set; } = "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))";

    /// <summary>
    /// Which LDAP provider to use: "ActiveDirectory" (System.DirectoryServices) or "StandardLdap" (Novell).
    /// </summary>
    public string ProviderType { get; set; } = "StandardLdap";

    /// <summary>
    /// Page size for reading users from LDAP (RFC 2696). It is what gets past the directory's
    /// own server-side size limit.
    /// </summary>
    public int UserSearchPageSize { get; set; } = 500;

    /// <summary>The port to connect to: <see cref="Port"/>, or the standard one for the mode.</summary>
    public int ResolvedPort => Port ?? LdapSecurityPorts.StandardPortFor(Security);

    /// <summary>
    /// These settings aimed at one particular endpoint — a failover host, say.
    ///
    /// A copy of everything rather than a list of the fields someone remembered: the copy this
    /// replaced was written out by hand, so a setting added later was silently missing from every
    /// directory reached through it. A new TLS setting dropped there would have meant a
    /// connection quietly made without TLS.
    /// </summary>
    public LdapSettings ForEndpoint(string server, int port)
    {
        var copy = (LdapSettings)MemberwiseClone();
        copy.Server = server;
        copy.Port = port;
        return copy;
    }
}
