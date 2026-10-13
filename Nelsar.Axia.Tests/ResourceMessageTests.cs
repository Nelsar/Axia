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
        Assert.Contains("DisplayName", DomainMessages.ValueRequired("DisplayName"));
        Assert.Contains("70000", DomainMessages.LdapPortOutOfRange(70000, 1, 65535));
        Assert.Contains("axia-admins", ApplicationMessages.GroupNotFound("axia-admins"));
    }

    /// <summary>
    /// The messages allowed to repeat a string they are given, and why it cannot be a person.
    /// Everything else that takes one must leave it out of the text.
    /// </summary>
    private static readonly Dictionary<string, string> RepeatsNoPerson = new()
    {
        [nameof(DomainMessages.LdapSecurityContradictsPort)] = "a configuration value",
        [nameof(DomainMessages.ValueRequired)] = "a field name",
        [nameof(DomainMessages.ValueTooLong)] = "a field name",
        [nameof(DomainMessages.GroupMappingNeedsDistinguishedName)] = "a group",
        [nameof(DomainMessages.PermissionUnknown)] = "a permission",
        [nameof(ApplicationMessages.BulkOperationUnknown)] = "an operation name",
        [nameof(ApplicationMessages.LdapSecurityUnknown)] = "a configuration value",
        [nameof(ApplicationMessages.DirectoryProviderUnknown)] = "a directory's id",
        [nameof(ApplicationMessages.DirectoryProviderUnreachable)] = "a directory's id",
        [nameof(ApplicationMessages.DirectoryProviderMisconfigured)] = "a directory's id",
        [nameof(ApplicationMessages.OrganizationalUnitNotFound)] = "an organizational unit",
        [nameof(ApplicationMessages.OrganizationalUnitLookupIncomplete)] = "an organizational unit",
        [nameof(ApplicationMessages.OrganizationalUnitAlreadyExists)] = "an organizational unit",
        [nameof(ApplicationMessages.OrganizationalUnitNotEmpty)] = "an organizational unit",
        [nameof(ApplicationMessages.GroupNotFound)] = "a group",
        [nameof(ApplicationMessages.GroupLookupIncomplete)] = "a group",
        [nameof(ApplicationMessages.GroupAlreadyExists)] = "a group",
        [nameof(ApplicationMessages.GroupNotEmptyContainer)] = "a group",
        [nameof(ApplicationMessages.ComputerNotFound)] = "a computer",
        [nameof(ApplicationMessages.ComputerLookupIncomplete)] = "a computer",
        [nameof(ApplicationMessages.ComputerAlreadyExists)] = "a computer",
        [nameof(ApplicationMessages.ComputerNotEmptyContainer)] = "a computer",
        [nameof(ApplicationMessages.RoleUnknown)] = "a role",
        [nameof(ApplicationMessages.RoleNotEditable)] = "a role",
        [nameof(ApplicationMessages.PermissionNotGrantable)] = "a permission",
        [nameof(ApplicationMessages.ParentOutsideBaseDn)] = "the configured base DN",
    };

    [Theory]
    [MemberData(nameof(MessageClasses))]
    public void NoMessage_RepeatsAnAccount(Type messagesType)
    {
        // These texts are returned to the caller, and they are also the {Reason} of every refusal
        // in the diagnostic log and the text of every logged exception — in full, because LogMask
        // can only mask what it is handed. A message that quotes a login or a DN puts it in
        // Logstash and Loki unmasked. So a message repeats what it is given only when that cannot
        // be a person, and says so here.
        const string account = "ivan.petrov@example.org";

        var repeating = messagesType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(string)))
            .Where(m =>
            {
                var arguments = m.GetParameters()
                    .Select(p => p.ParameterType == typeof(int) ? (object)1 : account)
                    .ToArray();
                return ((string)m.Invoke(null, arguments)!).Contains(account, StringComparison.Ordinal);
            })
            .Select(m => m.Name)
            .ToList();

        Assert.All(repeating, name => Assert.True(RepeatsNoPerson.ContainsKey(name),
            $"{messagesType.Name}.{name} repeats its argument to the caller and to the log. Leave it out " +
            $"of the text, or, if it cannot be a person, add {name} to {nameof(RepeatsNoPerson)} with the reason."));
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
        // A translation that drops {0} compiles, ships, and tells an administrator which group was
        // not found by leaving the name out of the sentence.
        var message = WithUiCulture(culture, () => ApplicationMessages.GroupNotFound("axia-admins"));

        Assert.Contains("axia-admins", message);
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
