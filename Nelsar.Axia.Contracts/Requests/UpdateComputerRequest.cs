namespace Nelsar.Axia.Contracts.Requests;

/// <summary>
/// A partial update: every property is optional, and null means "leave this as it is".
///
/// The distinction matters for the free-text fields — null keeps the current value, an empty
/// string clears it. A non-nullable default of "" would wipe a machine's description and DNS
/// name on every rename, which is the same trap <see cref="UpdateGroupRequest"/> documents.
///
/// Renaming a computer account here renames the directory entry only. The machine keeps the
/// name it knows itself by until it is renamed on the machine and rejoined; the two are not
/// kept in step by this API.
/// </summary>
public class UpdateComputerRequest
{
    public string? Name { get; set; }

    /// <summary>A different parent moves the account. Null leaves it where it is.</summary>
    public string? ParentDistinguishedName { get; set; }

    public string? Description { get; set; }

    public string? DnsHostName { get; set; }

    /// <summary>
    /// Toggles bit 0x2 (ACCOUNTDISABLE) of <c>userAccountControl</c>. Null leaves the account's
    /// state alone. Disabling breaks the machine's ability to authenticate to the domain.
    /// </summary>
    public bool? Enabled { get; set; }
}
