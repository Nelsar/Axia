namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// A user as it exists in the directory, as read through the LDAP port.
/// This is the contract every <c>ILdapProvider</c> implementation produces and every
/// <c>ILdapService</c> consumer receives — the directory's own identity (<see cref="Dn"/>)
/// plus the attributes the application actually uses, already normalised across
/// Active Directory and OpenLDAP attribute naming.
/// </summary>
public sealed class LdapUserInfo
{
    public string Dn { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string[] Groups { get; init; } = [];
}
