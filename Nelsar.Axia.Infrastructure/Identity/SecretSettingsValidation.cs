using System.Text;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>The <c>Encryption</c> section: the AES key bind passwords are stored under.</summary>
internal sealed class EncryptionSettings
{
    public const string SectionName = "Encryption";

    /// <summary>Base64 of a 16, 24 or 32-byte AES key.</summary>
    public string? Key { get; set; }
}

/// <summary>
/// What makes a configured secret unusable, shared by the two keys this application cannot run
/// safely without.
///
/// Both used to be accepted as they came. A missing or malformed <c>Encryption:Key</c> did not
/// stop anything: the encryption service logged a warning and stored every bind password in
/// plaintext from then on — and the placeholder in the example configuration is not valid base64,
/// so copying the example produced exactly that. The placeholder <c>Jwt:Key</c>, on the other
/// hand, is long enough to be a perfectly good signing key, and it is published in this
/// repository: anyone who has read it could mint a token naming any account, administrators
/// included. Both now stop the application at startup, with a message naming the setting.
/// </summary>
internal static class SecretRequirements
{
    /// <summary>The marker every placeholder in the example configuration carries.</summary>
    private const string PlaceholderMarker = "CHANGE_ME";

    /// <summary>HS256 wants a key at least as long as its 256-bit output.</summary>
    public const int MinimumJwtKeyBytes = 32;

    public static string? JwtKeyProblem(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Jwt:Key is not set. Generate one with `openssl rand -base64 48` and supply it through "
                + "configuration or the Jwt__Key environment variable.";

        if (key.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase))
            return "Jwt:Key is still the placeholder from the example configuration. That value is public, "
                + "so anyone could sign tokens with it. Generate a key with `openssl rand -base64 48`.";

        if (Encoding.UTF8.GetByteCount(key) < MinimumJwtKeyBytes)
            return $"Jwt:Key is shorter than {MinimumJwtKeyBytes} bytes, which is too short to sign tokens "
                + "with HS256. Generate a key with `openssl rand -base64 48`.";

        return null;
    }

    public static string? EncryptionKeyProblem(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Encryption:Key is not set, so directory bind passwords cannot be stored encrypted. "
                + "Generate one with `openssl rand -base64 32` and supply it through configuration or the "
                + "Encryption__Key environment variable.";

        if (key.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase))
            return "Encryption:Key is still the placeholder from the example configuration. Generate a key "
                + "with `openssl rand -base64 32`.";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            return "Encryption:Key is not valid base64. Generate a key with `openssl rand -base64 32`.";
        }

        if (bytes.Length is not (16 or 24 or 32))
            return $"Encryption:Key decodes to {bytes.Length} bytes; an AES key is 16, 24 or 32. "
                + "Generate one with `openssl rand -base64 32`.";

        return null;
    }
}

internal sealed class JwtSettingsValidation : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings options) =>
        SecretRequirements.JwtKeyProblem(options.Key) is { } problem
            ? ValidateOptionsResult.Fail(problem)
            : ValidateOptionsResult.Success;
}

internal sealed class EncryptionSettingsValidation : IValidateOptions<EncryptionSettings>
{
    public ValidateOptionsResult Validate(string? name, EncryptionSettings options) =>
        SecretRequirements.EncryptionKeyProblem(options.Key) is { } problem
            ? ValidateOptionsResult.Fail(problem)
            : ValidateOptionsResult.Success;
}
