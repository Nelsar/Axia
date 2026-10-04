using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.Common;

/// <summary>
/// The two checks every entity in here repeats: a field that must carry text, and a field whose
/// text must fit the column it is stored in.
///
/// Shared rather than written out per property because the second one is the check people skip.
/// A name longer than its column does not fail in the aggregate that accepted it — it fails at
/// SaveChanges, as a provider-specific truncation or constraint error, with nothing in the
/// message naming the property. Failing here instead turns that into a 400 that says which field.
/// </summary>
internal static class DomainGuard
{
    /// <summary>
    /// The trimmed value, or a validation failure naming <paramref name="field"/>.
    /// </summary>
    /// <exception cref="DomainValidationException">Blank, or longer than the column allows.</exception>
    public static string RequiredText(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainValidationException(DomainMessages.ValueRequired(field));

        var trimmed = value.Trim();

        if (trimmed.Length > maximumLength)
            throw new DomainValidationException(DomainMessages.ValueTooLong(field, maximumLength));

        return trimmed;
    }

    /// <summary>
    /// The trimmed value, or null. Blank collapses to null so that "" and null — which arrive
    /// interchangeably from JSON and from a cleared form field — do not become two states.
    /// </summary>
    /// <exception cref="DomainValidationException">Longer than the column allows.</exception>
    public static string? OptionalText(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (trimmed.Length > maximumLength)
            throw new DomainValidationException(DomainMessages.ValueTooLong(field, maximumLength));

        return trimmed;
    }
}
