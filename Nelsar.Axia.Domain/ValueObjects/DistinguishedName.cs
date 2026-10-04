using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.ValueObjects;

/// <summary>
/// An LDAP distinguished name, e.g. "CN=Administrators,OU=Groups,DC=example,DC=com".
///
/// Comparison is case-insensitive: RFC 4511 directories treat attribute types that way, and
/// AD returns group DNs with inconsistent casing between operations, so an ordinal comparison
/// would silently miss matches.
///
/// The format is intentionally not validated beyond "not blank". DNs arrive from live
/// directories, which contain entries no parser we write here should get a veto over —
/// rejecting them would lose users rather than protect anything.
/// </summary>
public sealed class DistinguishedName : IEquatable<DistinguishedName>
{
    private DistinguishedName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <exception cref="DomainValidationException">The value is missing or blank.</exception>
    public static DistinguishedName Create(string? value)
    {
        if (!TryCreate(value, out var dn))
            throw new DomainValidationException(DomainMessages.DistinguishedNameBlank());

        return dn!;
    }

    public static bool TryCreate(string? value, out DistinguishedName? distinguishedName)
    {
        distinguishedName = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        distinguishedName = new DistinguishedName(value.Trim());
        return true;
    }

    /// <summary>
    /// The value of the first CN component, which is how a group DN maps onto the group name
    /// a role mapping is configured with. Falls back to the whole DN when there is no CN
    /// component, so a caller comparing names still has something meaningful to match on.
    /// </summary>
    public string Cn
    {
        get
        {
            foreach (var part in Value.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return trimmed[3..];
            }

            return Value;
        }
    }

    /// <summary>
    /// The value of the leftmost component — "Finance" for "OU=Finance,DC=example,DC=com".
    /// That is the entry's own name within its parent, and the part a rename replaces.
    /// </summary>
    public string Rdn
    {
        get
        {
            var first = SplitComponents(Value).FirstOrDefault() ?? Value;
            var separator = first.IndexOf('=');
            return separator < 0 ? first : UnescapeRdnValue(first[(separator + 1)..]);
        }
    }

    /// <summary>
    /// Everything to the right of the leftmost component — the container this entry sits in,
    /// and the part a move replaces. Empty for a single-component DN, which has no parent.
    /// </summary>
    public string Parent
    {
        get
        {
            var components = SplitComponents(Value).ToList();
            return components.Count <= 1 ? string.Empty : string.Join(",", components.Skip(1));
        }
    }

    /// <summary>
    /// Splits on commas that actually separate components, ignoring those escaped as "\,"
    /// inside a value. Directory entries really do contain commas in their names, and a naive
    /// Split(',') turns one such entry into two nonexistent ones.
    /// </summary>
    private static IEnumerable<string> SplitComponents(string dn)
    {
        var current = new System.Text.StringBuilder();
        var escaped = false;

        foreach (var c in dn)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }

            switch (c)
            {
                case '\\':
                    current.Append(c);
                    escaped = true;
                    break;
                case ',':
                    yield return current.ToString().Trim();
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }

    /// <summary>
    /// Escapes an attribute value for use in an RDN, per RFC 4514. Without this, a unit named
    /// "Human Resources, Northern Branch" would be written as two DN components and land
    /// somewhere nobody asked for.
    /// </summary>
    public static string EscapeRdnValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var builder = new System.Text.StringBuilder(trimmed.Length + 8);
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            var needsEscape = c is '\\' or ',' or '+' or '"' or '<' or '>' or ';' or '='
                              || (i == 0 && c is '#' or ' ')
                              || (i == trimmed.Length - 1 && c == ' ');

            if (needsEscape)
                builder.Append('\\');

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string UnescapeRdnValue(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var builder = new System.Text.StringBuilder(value.Length);
        var escaped = false;

        foreach (var c in value)
        {
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public bool Equals(DistinguishedName? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as DistinguishedName);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(DistinguishedName? left, DistinguishedName? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(DistinguishedName? left, DistinguishedName? right) => !(left == right);
}
