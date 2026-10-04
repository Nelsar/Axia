namespace Nelsar.Axia.Application.Interfaces;

/// <summary>
/// Port for at-rest protection of bind credentials. Lives in the application layer
/// because application services (e.g. <see cref="Services.ProviderService"/>) depend
/// on it; the AES-GCM implementation lives in the infrastructure layer.
/// </summary>
public interface IEncryptionService
{
    string Decrypt(string value);

    /// <summary>
    /// Encrypts a plaintext secret and returns a transport-safe representation.
    /// Implementations should return values prefixed with "enc:" so callers
    /// can detect encrypted payloads. If encryption is not available, the
    /// implementation may return the original value.
    /// </summary>
    string Encrypt(string plainText);
}
