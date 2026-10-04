namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// A computer account as it exists in the directory, as read through the LDAP port.
///
/// <see cref="Dn"/> is the identity, on the same terms as <see cref="LdapGroup"/> and
/// <see cref="LdapOrganizationalUnit"/>: the directory addresses the entry by it, and every
/// write names it. <see cref="Name"/> and <see cref="ParentDn"/> are derived from the DN and
/// carried separately only so callers do not each parse it.
///
/// The remaining fields are Active Directory's. A computer account is an AD concept — its
/// <c>computer</c> class has no equivalent in RFC 4519, where the nearest thing is
/// <c>device</c>, which carries none of this. Against a directory that reports none of them,
/// each field is simply empty or null, which is why <see cref="Enabled"/> is nullable: an
/// account whose state cannot be read must not be presented as an enabled one.
/// </summary>
public sealed class LdapComputer
{
    public string Dn { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ParentDn { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>The machine's fully qualified DNS name, when the directory records one.</summary>
    public string DnsHostName { get; init; } = string.Empty;

    public string OperatingSystem { get; init; } = string.Empty;
    public string OperatingSystemVersion { get; init; } = string.Empty;

    /// <summary>
    /// Read from bit 0x2 (ACCOUNTDISABLE) of <c>userAccountControl</c>. Null when the directory
    /// reports no such attribute — "we could not tell" rather than "enabled".
    /// </summary>
    public bool? Enabled { get; init; }
}
