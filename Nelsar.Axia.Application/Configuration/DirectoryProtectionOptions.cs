using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Configuration;

/// <summary>
/// Groups whose members — and which themselves — only an administrator of this application may
/// change through it, on top of whatever Active Directory marks privileged itself.
///
/// Active Directory needs nothing here: adminCount=1 already marks Domain Admins and the other
/// protected groups and everyone in them, nested membership included. OpenLDAP has no such mark,
/// so the groups that make someone a directory administrator there have to be named.
/// </summary>
public sealed class DirectoryProtectionOptions
{
    public const string SectionName = "DirectoryProtection";

    /// <summary>Distinguished names, compared without regard to case or spacing.</summary>
    public string[] ProtectedGroups { get; set; } = [];

    public bool Protects(string? groupDn) =>
        DistinguishedName.TryCreate(groupDn, out var group)
        && ProtectedGroups.Any(listed =>
            DistinguishedName.TryCreate(listed, out var protectedGroup)
            && string.Equals(group!.Normalized, protectedGroup!.Normalized, StringComparison.OrdinalIgnoreCase));
}
