namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// A group as it exists in the directory, as read through the LDAP port.
///
/// <see cref="Dn"/> is the identity, for the same reason it is for
/// <see cref="LdapOrganizationalUnit"/>: it is what the directory addresses the entry by, and
/// every write (rename, move, delete) names it. <see cref="Name"/> and <see cref="ParentDn"/>
/// are derived from the DN and carried separately only so callers do not each parse it.
///
/// <see cref="Members"/> holds member distinguished names, not logins — that is what the
/// <c>member</c> attribute of <c>groupOfNames</c> and of Active Directory's <c>group</c>
/// contains. A group with no members is a real state and is reported as an empty list; a
/// membership that could not be read is not, and arrives as an exception instead.
/// </summary>
public sealed class LdapGroup
{
    public string Dn { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ParentDn { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();
}
