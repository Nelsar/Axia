namespace Nelsar.Axia.Contracts.Common;

public class ComputerDto
{
    /// <summary>
    /// A hash of <see cref="DistinguishedName"/>, so it is stable without anything being stored
    /// — and it changes when the account is renamed or moved, because the DN is the computer's
    /// identity as far as the directory is concerned.
    /// </summary>
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DistinguishedName { get; set; } = string.Empty;
    public string ParentDistinguishedName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string DnsHostName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string OperatingSystemVersion { get; set; } = string.Empty;

    /// <summary>Null when the directory reports no userAccountControl — unknown, not enabled.</summary>
    public bool? Enabled { get; set; }
}
