using System.Globalization;
using System.Resources;

namespace Nelsar.Axia.Domain.Resources;

/// <summary>
/// Typed access to <c>DomainMessages.resx</c> — the text of failures the domain reports to a
/// caller. Hand-written rather than designer-generated so the accessors can take real
/// parameters: a caller cannot forget an argument or pass them in the wrong order.
///
/// Adding a translation means dropping in <c>DomainMessages.&lt;culture&gt;.resx</c>; the
/// satellite assembly is picked up through <see cref="CultureInfo.CurrentUICulture"/> with no
/// change here. Log messages deliberately stay out of these files — see the note in
/// ApplicationMessages.
/// </summary>
public static class DomainMessages
{
    private static readonly ResourceManager Manager = new(
        "Nelsar.Axia.Domain.Resources.DomainMessages",
        typeof(DomainMessages).Assembly);

    /// <param name="value">The rejected address.</param>
    public static string EmailInvalid(string? value) =>
        Format(nameof(EmailInvalid), value);

    public static string DistinguishedNameBlank() =>
        Get(nameof(DistinguishedNameBlank));

    public static string LdapPortOutOfRange(int value, int minimum, int maximum) =>
        Format(nameof(LdapPortOutOfRange), value, minimum, maximum);

    /// <param name="security">The mode, as its name: Ldaps, StartTls or None.</param>
    /// <param name="port">The port it was given.</param>
    /// <param name="standardPort">The port that mode is normally served on.</param>
    public static string LdapSecurityContradictsPort(string security, int port, int standardPort) =>
        Format(nameof(LdapSecurityContradictsPort), security, port, standardPort);

    /// <param name="field">The property whose value is missing.</param>
    public static string ValueRequired(string field) =>
        Format(nameof(ValueRequired), field);

    /// <param name="field">The property that is too long.</param>
    /// <param name="maximum">The greatest number of characters accepted.</param>
    public static string ValueTooLong(string field, int maximum) =>
        Format(nameof(ValueTooLong), field, maximum);

    public static string SearchFilterPlaceholderMissing() =>
        Get(nameof(SearchFilterPlaceholderMissing));

    public static string SearchFilterMalformed() =>
        Get(nameof(SearchFilterMalformed));

    public static string SearchPageSizeOutOfRange(int value, int minimum, int maximum) =>
        Format(nameof(SearchPageSizeOutOfRange), value, minimum, maximum);

    public static string PriorityNegative(int value) =>
        Format(nameof(PriorityNegative), value);

    public static string RateLimitPermitLimitInvalid(int value) =>
        Format(nameof(RateLimitPermitLimitInvalid), value);

    public static string RateLimitWindowInvalid(int value) =>
        Format(nameof(RateLimitWindowInvalid), value);

    public static string RateLimitQueueLimitNegative(int value) =>
        Format(nameof(RateLimitQueueLimitNegative), value);

    /// <param name="value">The permission that is not in the catalogue.</param>
    public static string PermissionUnknown(string value) =>
        Format(nameof(PermissionUnknown), value);

    // A missing entry returns the key rather than throwing: a gap in a translation should show
    // up as an odd-looking string, not take down the request that hit it.
    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
}
