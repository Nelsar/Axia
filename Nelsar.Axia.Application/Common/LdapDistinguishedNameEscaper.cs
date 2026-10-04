namespace Nelsar.Axia.Application.Common;

/// <summary>
/// RFC 4514 escaping for a value placed inside a distinguished name.
///
/// A sibling to <see cref="LdapFilterEscaper"/>, and not the same rules: filters (RFC 4515) and
/// DNs (RFC 4514) escape different characters, so using one where the other belongs looks correct
/// and protects nothing. Building <c>CN={name},{baseDn}</c> from an unescaped value lets a comma
/// in the value close the RDN and append components of the attacker's choosing — the DN equivalent
/// of filter injection.
/// </summary>
public static class LdapDistinguishedNameEscaper
{
    private const string MustEscape = "\\,+\"<>;=";

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var escaped = new System.Text.StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            // A leading '#' or space, and a trailing space, are only special at the edges — where
            // they change how the whole value is read rather than where it ends.
            var atStart = index == 0;
            var atEnd = index == value.Length - 1;

            if (MustEscape.Contains(character, StringComparison.Ordinal)
                || (atStart && (character == '#' || character == ' '))
                || (atEnd && character == ' '))
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }
}
