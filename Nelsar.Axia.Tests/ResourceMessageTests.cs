using System.Globalization;
using System.Reflection;
using Nelsar.Axia.Api.Resources;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Guards the one failure mode a resource file fails at silently: if the .resx is not embedded,
/// or its logical name does not match the base name the ResourceManager was constructed with,
/// every lookup misses and the accessor returns the key. Nothing throws, the build is green, and
/// clients start receiving "UserAlreadyExists" as an error message.
///
/// Rather than pin one message per class, this walks every accessor there is, so a new entry
/// added to the class but forgotten in the .resx fails here instead of in production.
/// </summary>
public class ResourceMessageTests
{
    public static TheoryData<Type> MessageClasses() =>
    [
        typeof(DomainMessages),
        typeof(ApplicationMessages),
        typeof(ApiMessages),
    ];

    [Theory]
    [MemberData(nameof(MessageClasses))]
    public void EveryAccessor_ResolvesToRealText(Type messagesType)
    {
        var accessors = messagesType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName)
            .ToList();

        Assert.NotEmpty(accessors);

        foreach (var accessor in accessors)
        {
            var arguments = accessor.GetParameters()
                .Select(p => p.ParameterType == typeof(int) ? (object)1 : "sample")
                .ToArray();

            var message = (string)accessor.Invoke(null, arguments)!;

            Assert.False(string.IsNullOrWhiteSpace(message),
                $"{messagesType.Name}.{accessor.Name} resolved to nothing.");

            // A missed lookup returns the key verbatim — that is exactly what must not happen.
            Assert.False(message == accessor.Name,
                $"{messagesType.Name}.{accessor.Name} has no entry in the .resx (it returned its own key).");

            // Placeholders must have been substituted, not left in the output.
            Assert.DoesNotContain("{0}", message);
        }
    }

    [Fact]
    public void ParameterisedMessages_SubstituteTheirArguments()
    {
        Assert.Contains("not-an-address", DomainMessages.EmailInvalid("not-an-address"));
        Assert.Contains("70000", DomainMessages.LdapPortOutOfRange(70000, 1, 65535));
        Assert.Contains("jsmith", ApplicationMessages.UserNotFoundInLdap("jsmith"));
    }

    [Fact]
    public void GenericFailureText_RevealsNothingAboutTheFailure()
    {
        var message = ApiMessages.UnexpectedError();

        Assert.DoesNotContain("ldap", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", message, StringComparison.OrdinalIgnoreCase);
    }

    private static T WithUiCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    public static TheoryData<string> TranslatedCultures()
    {
        var data = new TheoryData<string>();
        foreach (var culture in SupportedCultures.Translated) data.Add(culture);
        return data;
    }

    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void SatelliteAssemblies_AreBuiltAndLoadable(string culture)
    {
        // One assertion per assembly that owns a .resx. A missing satellite shows up as English
        // text rather than as any kind of error, so it has to be asserted rather than assumed —
        // and the failure is per-assembly, because the three are built independently.
        foreach (var (name, accessor) in new (string, Func<string>)[]
                 {
                     (nameof(DomainMessages), DomainMessages.DistinguishedNameBlank),
                     (nameof(ApplicationMessages), ApplicationMessages.UserAlreadyExists),
                     (nameof(ApiMessages), ApiMessages.InvalidCredentials),
                 })
        {
            Assert.False(accessor() == WithUiCulture(culture, accessor),
                $"{name} has no loadable {culture} satellite assembly — its neutral text came back.");
        }
    }

    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void TranslatedMessages_KeepTheirPlaceholders(string culture)
    {
        // A translation that drops {0} compiles, ships, and tells an administrator which user was
        // not found by leaving the name out of the sentence.
        var message = WithUiCulture(culture, () => ApplicationMessages.UserNotFoundInLdap("jsmith"));

        Assert.Contains("jsmith", message);
        Assert.DoesNotContain("{0}", message);
    }

    [Fact]
    public void UnknownCulture_FallsBackToTheNeutralText()
    {
        // A client asking for a language we do not carry must still get something readable.
        Assert.Equal(ApiMessages.InvalidCredentials(),
            WithUiCulture("ja-JP", ApiMessages.InvalidCredentials));
    }

    public static TheoryData<Type, string> MessageClassesByCulture()
    {
        var data = new TheoryData<Type, string>();
        foreach (var type in new[] { typeof(DomainMessages), typeof(ApplicationMessages), typeof(ApiMessages) })
            foreach (var culture in SupportedCultures.Translated)
                data.Add(type, culture);
        return data;
    }

    [Theory]
    [MemberData(nameof(MessageClassesByCulture))]
    public void EveryAccessor_IsTranslated(Type messagesType, string culture)
    {
        // The cross product, so adding a language is not something that can be half-done: the
        // .resx for it either carries every message or this fails by name. It walks the accessors
        // rather than listing them, so a message added to the class and forgotten in a translation
        // fails here too.
        var accessors = messagesType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName)
            .ToList();

        Assert.NotEmpty(accessors);

        foreach (var accessor in accessors)
        {
            var arguments = accessor.GetParameters()
                .Select(p => p.ParameterType == typeof(int) ? (object)1 : "sample")
                .ToArray();

            var neutral = (string)accessor.Invoke(null, arguments)!;
            var translated = WithUiCulture(culture, () => (string)accessor.Invoke(null, arguments)!);

            // Identical text means the entry is missing and the neutral one was served instead.
            Assert.False(neutral == translated,
                $"{messagesType.Name}.{accessor.Name} has no entry in the .{culture}.resx.");
        }
    }
}
