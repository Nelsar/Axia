namespace Nelsar.Axia.Application.Configuration;

public class LdapSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
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
}
