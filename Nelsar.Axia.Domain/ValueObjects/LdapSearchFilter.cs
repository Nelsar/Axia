using System.Globalization;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.ValueObjects;

/// <summary>
/// The RFC 4515 filter template a directory is searched with, carrying one placeholder for the
/// login being looked up.
///
/// It was a free-text string on <see cref="Entities.Provider"/>, set by an administrator and
/// handed straight to <c>string.Format</c> before every user search. Three things could go wrong
/// there and only the third was noisy: a template with no placeholder searched for the literal
/// template and silently matched nobody; a template with <c>{1}</c> threw FormatException from
/// inside the directory call, surfacing as a 500 on login; unbalanced parentheses were rejected
/// by the server as a protocol error. All three are properties of the template alone, so they
/// belong to the type rather than to each of the four call sites that formats one.
///
/// What this type does <em>not</em> do is make the formatted result safe. The value substituted
/// in must already be escaped by the caller — see <c>LdapFilterEscaper</c>. A template is
/// configuration, written by an administrator; the login is input, and the two are guarded
/// differently on purpose.
/// </summary>
public sealed class LdapSearchFilter : IEquatable<LdapSearchFilter>
{
    /// <summary>Matches the column width the template is stored in.</summary>
    public const int MaximumLength = 512;

    /// <summary>
    /// Matches AD (sAMAccountName/userPrincipalName), OpenLDAP/eDirectory (uid), or email login
    /// in a single query, so the default works against any of the three out of the box.
    /// </summary>
    private const string DefaultTemplate =
        "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))";

    public static LdapSearchFilter Default { get; } = new(DefaultTemplate);

    private LdapSearchFilter(string template) => Template = template;

    /// <summary>The raw template, placeholder included.</summary>
    public string Template { get; }

    /// <exception cref="DomainValidationException">
    /// The template is blank, too long, carries no login placeholder, or is not a well-formed
    /// filter.
    /// </exception>
    public static LdapSearchFilter Create(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new DomainValidationException(DomainMessages.ValueRequired(nameof(Entities.Provider.UserSearchFilter)));

        var trimmed = template.Trim();

        if (trimmed.Length > MaximumLength)
            throw new DomainValidationException(
                DomainMessages.ValueTooLong(nameof(Entities.Provider.UserSearchFilter), MaximumLength));

        if (!trimmed.Contains("{0}", StringComparison.Ordinal))
            throw new DomainValidationException(DomainMessages.SearchFilterPlaceholderMissing());

        if (!IsWellFormed(trimmed))
            throw new DomainValidationException(DomainMessages.SearchFilterMalformed());

        return new LdapSearchFilter(trimmed);
    }

    /// <summary>
    /// Reads a template that is already in the database, without re-applying the rules above.
    ///
    /// Deliberately lenient, unlike <see cref="LdapPort"/>'s converter which validates on read.
    /// A port outside 1-65535 could never have been written by any version of this code, so
    /// validating a stored one costs nothing; a filter without a placeholder <em>could</em> have
    /// been, since nothing checked before this type existed. Rejecting it on read would fail the
    /// GET that an administrator needs in order to see the bad value and fix it — locking them
    /// out of the only screen that can repair the row. A blank falls back to
    /// <see cref="Default"/>, which is the behaviour the column's own default already describes.
    /// </summary>
    public static LdapSearchFilter FromStorage(string? template) =>
        string.IsNullOrWhiteSpace(template) ? Default : new LdapSearchFilter(template.Trim());

    /// <summary>
    /// Substitutes the login into the template.
    /// </summary>
    /// <param name="escapedValue">
    /// The login, already escaped per RFC 4515. Passing a raw one is an LDAP injection.
    /// </param>
    public string Format(string escapedValue) =>
        string.Format(CultureInfo.InvariantCulture, Template, escapedValue);

    /// <summary>
    /// Parentheses balanced and the format string usable. The second half is checked by running
    /// the substitution rather than by parsing: <c>string.Format</c> is what will run in
    /// production, so its own verdict on <c>{1}</c>, a stray brace or a bad format specifier is
    /// the only one that matters.
    /// </summary>
    private static bool IsWellFormed(string template)
    {
        var depth = 0;
        foreach (var character in template)
        {
            switch (character)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    if (--depth < 0)
                        return false;
                    break;
            }
        }

        if (depth != 0)
            return false;

        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, template, "probe");
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool Equals(LdapSearchFilter? other) =>
        other is not null && string.Equals(Template, other.Template, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as LdapSearchFilter);

    public override int GetHashCode() => Template.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Template;

    public static bool operator ==(LdapSearchFilter? left, LdapSearchFilter? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(LdapSearchFilter? left, LdapSearchFilter? right) => !(left == right);
}
