namespace Nelsar.Axia.Contracts.Common;

public class GroupDto
{
    /// <summary>
    /// A hash of <see cref="DistinguishedName"/>, so it is stable without anything being stored
    /// — and it changes when the group is renamed or moved, because the DN is the group's
    /// identity as far as the directory is concerned.
    /// </summary>
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DistinguishedName { get; set; } = string.Empty;
    public string ParentDistinguishedName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Member distinguished names, as the directory stores them — not logins.</summary>
    public IEnumerable<string> Members { get; set; } = Array.Empty<string>();
}
