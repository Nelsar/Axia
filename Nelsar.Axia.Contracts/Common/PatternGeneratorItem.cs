namespace Nelsar.Axia.Contracts.Common;

public class PatternGeneratorItem
{
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// True if a matching LDAP object already exists (conflict).
    /// </summary>
    public bool Exists { get; set; }
}
