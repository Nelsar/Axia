namespace Nelsar.Axia.Domain.ValueObjects;

/// <summary>
/// A credential in its at-rest form — "enc:&lt;nonce&gt;:&lt;cipher&gt;:&lt;tag&gt;", as produced by
/// IEncryptionService.
///
/// The type earns its place through <see cref="ToString"/>. A bind password held as a bare
/// <c>string</c> is one careless interpolation away from a log line or an exception message, and
/// that is how secrets actually escape — not through a deliberate decision, but through
/// <c>$"provider {p.Name} pwd {p.EncryptedAdminPassword}"</c> written while debugging something
/// else. Here the same line prints <see cref="Mask"/>. Reaching the real value takes
/// <see cref="Reveal"/>, which is a deliberate act, greppable in review, and used in exactly one
/// place: where the ciphertext is handed to the decryptor.
///
/// Encryption itself stays in Infrastructure. This type carries the result, never the key.
/// </summary>
public sealed class EncryptedSecret : IEquatable<EncryptedSecret>
{
    /// <summary>What the secret renders as everywhere text is produced.</summary>
    public const string Mask = "***";

    /// <summary>No credential configured — an anonymous bind, not a blank password.</summary>
    public static EncryptedSecret None { get; } = new(string.Empty);

    private readonly string _cipherText;

    private EncryptedSecret(string cipherText) => _cipherText = cipherText;

    public bool IsEmpty => _cipherText.Length == 0;

    /// <summary>
    /// Wraps an already-encrypted value. Null and empty both mean "none": the column has always
    /// been non-nullable with an empty-string default, so the two arrive interchangeably from
    /// storage and must not become two different states here.
    /// </summary>
    public static EncryptedSecret FromCipherText(string? cipherText) =>
        string.IsNullOrEmpty(cipherText) ? None : new EncryptedSecret(cipherText);

    /// <summary>
    /// The stored ciphertext. Named to stand out at a call site: every use is a place where a
    /// secret leaves the type's protection, so every use should be worth reading twice.
    /// </summary>
    public string Reveal() => _cipherText;

    public bool Equals(EncryptedSecret? other) =>
        other is not null && string.Equals(_cipherText, other._cipherText, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as EncryptedSecret);

    public override int GetHashCode() => _cipherText.GetHashCode(StringComparison.Ordinal);

    /// <summary>Always the mask, never the secret — the reason this type exists.</summary>
    public override string ToString() => Mask;

    public static bool operator ==(EncryptedSecret? left, EncryptedSecret? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(EncryptedSecret? left, EncryptedSecret? right) => !(left == right);
}
