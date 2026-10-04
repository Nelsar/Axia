namespace Nelsar.Axia.Api.Resources;

/// <summary>
/// The languages this API answers in.
///
/// One list, in one place, because it is read by two things that must not disagree:
/// <c>RequestLocalizationOptions</c> in Program.cs, which decides what an Accept-Language header
/// may select, and ResourceMessageTests, which checks that every message is actually translated
/// into each of them. Written twice, the two drift the moment a language is added — and the
/// symptom of that drift is a culture accepted at the edge whose .resx nobody ever filled in,
/// which shows up as English text in the middle of a translated page rather than as any kind of
/// error.
///
/// Adding a language here is not enough on its own: it needs a satellite .resx beside each of the
/// three neutral files (ApiMessages, ApplicationMessages, DomainMessages) and an entry in the
/// frontend's LANGUAGES registry. The test is what makes the first of those impossible to forget.
/// </summary>
public static class SupportedCultures
{
    /// <summary>
    /// The neutral culture: the text that lives in the .resx files without a suffix, and what a
    /// client asking for a language we do not carry falls back to.
    /// </summary>
    public const string Neutral = "en";

    /// <summary>
    /// Everything with a satellite .resx of its own. The neutral culture is deliberately absent:
    /// a test asserting that "en" differs from the neutral text would be asserting nonsense.
    /// </summary>
    public static readonly string[] Translated = ["ru", "kk", "de", "es", "fr", "zh", "ar", "he"];

    /// <summary>What RequestLocalizationOptions accepts, neutral included.</summary>
    public static string[] All { get; } = [Neutral, .. Translated];
}
