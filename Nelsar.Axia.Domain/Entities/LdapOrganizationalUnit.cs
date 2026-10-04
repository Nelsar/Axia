namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// An organizational unit as it exists in the directory, as read through the LDAP port.
///
/// <see cref="Dn"/> is the identity: it is what the directory itself uses, and every write
/// (rename, move, delete) addresses the entry by it. <see cref="Name"/> and
/// <see cref="ParentDn"/> are both derivable from the DN, and are carried separately only so
/// callers do not each re-implement the parsing.
/// </summary>
public sealed class LdapOrganizationalUnit
{
    public string Dn { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ParentDn { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
