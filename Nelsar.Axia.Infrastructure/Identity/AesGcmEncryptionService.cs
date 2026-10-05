using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Interfaces;

namespace Nelsar.Axia.Infrastructure.Identity;

internal sealed class AesGcmEncryptionService : IEncryptionService
{
    private readonly byte[]? _key;
    private readonly ILogger<AesGcmEncryptionService> _logger;

    public AesGcmEncryptionService(IConfiguration configuration, ILogger<AesGcmEncryptionService> logger)
    {
        _logger = logger;
        var keyBase64 = configuration["Encryption:Key"];

        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            return;
        }

        try
        {
            var key = Convert.FromBase64String(keyBase64);
            if (key.Length is 16 or 24 or 32)
            {
                _key = key;
            }
            else
            {
                _logger.LogWarning("Encryption:Key must be a base64-encoded 16/24/32-byte AES key.");
            }
        }
        catch (FormatException)
        {
            _logger.LogWarning("Encryption:Key is not a valid base64 string.");
        }
    }

    public string Decrypt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!value.StartsWith("enc:", StringComparison.Ordinal))
        {
            return value;
        }

        if (_key == null)
        {
            throw new InvalidOperationException("Encrypted secret received, but Encryption:Key is not configured.");
        }

        var parts = value.Split(':', StringSplitOptions.None);
        if (parts.Length != 4)
        {
            throw new FormatException("Encrypted payload format must be enc:<nonceB64>:<cipherB64>:<tagB64>.");
        }

        var nonce = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var tag = Convert.FromBase64String(parts[3]);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        // Refused rather than passed through. This used to log a warning and return the plaintext,
        // which the caller then stored as if it were encrypted — so a missing or mistyped key
        // quietly put every bind password in the database in the clear. Startup now refuses such
        // a key (see SecretRequirements); this is the backstop for anything that gets here anyway.
        if (_key == null)
            throw new InvalidOperationException(
                "Encryption:Key is not configured, so the secret cannot be stored encrypted.");

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);

        var nonceB64 = Convert.ToBase64String(nonce);
        var cipherB64 = Convert.ToBase64String(cipher);
        var tagB64 = Convert.ToBase64String(tag);

        return $"enc:{nonceB64}:{cipherB64}:{tagB64}";
    }
}
