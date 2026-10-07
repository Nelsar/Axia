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

    /// <summary>
    /// The directory itself treats this account as privileged — in Active Directory, adminCount=1,
    /// which SDProp sets on every member of Domain Admins, Enterprise Admins, Administrators and the
    /// other protected groups, nested membership included.
    ///
    /// Carried because the application's own roles say nothing about it. A domain administrator
    /// whose groups map to no role is a plain User here, and judging a target only by the roles it
    /// maps to let anyone allowed to edit Users edit them too.
    /// </summary>
    public bool IsPrivileged { get; init; }

    /// <summary>
    /// The account this application binds to the directory as. Every write the application makes
    /// is made with it, so changing it through the application — its password above all — either
    /// breaks the application or hands its rights to whoever chose the new password.
    /// </summary>
    public bool IsServiceAccount { get; init; }
}
