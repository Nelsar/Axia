namespace Nelsar.Axia.Contracts.Requests;

public class PatternGeneratorRequest
{
    /// <summary>
    /// Mask pattern, e.g. "node-{01..99}". Used when RawText/RegexPattern are not supplied.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    public int MaxItems { get; set; } = 50;

    /// <summary>
    /// Raw pasted text (one record per line) to extract fields from via RegexPattern.
    /// </summary>
    public string? RawText { get; set; }

    /// <summary>
    /// Regex applied per line of RawText. Supports named groups, e.g. "(?&lt;lastname&gt;\S+)\s+(?&lt;firstname&gt;\S+)".
    /// </summary>
    public string? RegexPattern { get; set; }

    /// <summary>
    /// Template used to build each generated value from the regex's named groups,
    /// e.g. "{firstname}.{lastname}". Falls back to the full regex match if omitted.
    /// </summary>
    public string? UsernameTemplate { get; set; }

    /// <summary>
    /// Transliterates generated values from Cyrillic to Latin (for sAMAccountName-style logins).
    /// </summary>
    public bool Transliterate { get; set; }

    /// <summary>
    /// When true, checks each generated value against LDAP and reports conflicts in Details,
    /// without writing anything (simulation/preview mode).
    /// </summary>
    public bool CheckConflicts { get; set; }
}
