using System.Globalization;
using System.Resources;

namespace Nelsar.Axia.Api.Resources;

/// <summary>
/// Typed access to <c>ApiMessages.resx</c> — text the API layer itself writes into a response
/// body, as opposed to text that comes up from a domain or application failure.
/// </summary>
public static class ApiMessages
{
    private static readonly ResourceManager Manager = new(
        "Nelsar.Axia.Api.Resources.ApiMessages",
        typeof(ApiMessages).Assembly);

    public static string InvalidCredentials() => Get(nameof(InvalidCredentials));

    public static string TooManyRequests() => Get(nameof(TooManyRequests));

    public static string UnexpectedError() => Get(nameof(UnexpectedError));

    public static string BulkRequestEmpty() => Get(nameof(BulkRequestEmpty));

    public static string PatternRequired() => Get(nameof(PatternRequired));

    /// <summary>
    /// A bearer session named a directory other than the one it was opened against. It says how
    /// to get what the caller wanted — sign in again — because refusing without that reads as a
    /// permissions problem, which it is not.
    /// </summary>
    public static string SessionBoundToAnotherDirectory() =>
        Get(nameof(SessionBoundToAnotherDirectory));

    /// <summary>
    /// Self-registration is switched off for this deployment. Phrased as "not available" rather
    /// than "you are not allowed": the endpoint does not exist here, and saying so is both true
    /// and less of an invitation than naming a feature someone might go looking for.
    /// </summary>
    public static string SelfRegistrationDisabled() => Get(nameof(SelfRegistrationDisabled));

    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
