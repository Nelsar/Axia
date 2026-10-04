namespace Nelsar.Axia.Domain.Services;

/// <summary>
/// Everything <see cref="UserPatternGenerator"/> needs to produce a pool of candidate
/// logins. Deliberately free of any I/O concern: checking the generated pool against a
/// live directory is the application layer's job, not part of generating it.
/// </summary>
public sealed record UserPatternSpec
{
    /// <summary>
    /// Mask pattern, e.g. "node-{01..99}". Used when <see cref="RawText"/> and
    /// <see cref="RegexPattern"/> are not both supplied.
    /// </summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>Raw pasted text, one record per line, parsed via <see cref="RegexPattern"/>.</summary>
    public string? RawText { get; init; }

    /// <summary>Regex applied per line of <see cref="RawText"/>. Supports named groups.</summary>
    public string? RegexPattern { get; init; }

    /// <summary>
    /// Template building each value from the regex's named groups, e.g. "{firstname}.{lastname}".
    /// Falls back to the full regex match when omitted.
    /// </summary>
    public string? UsernameTemplate { get; init; }

    /// <summary>Transliterates generated values from Cyrillic to Latin.</summary>
    public bool Transliterate { get; init; }

    /// <summary>Upper bound on how many values are produced.</summary>
    public int MaxItems { get; init; } = 50;
}
