using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;

namespace Nelsar.Axia.Domain.ValueObjects;

/// <summary>
/// A validated, normalised email address.
///
/// Normalisation is trim + lower-case. That matters for an identity system: without it
/// "User@Example.com" and "user@example.com" create two accounts for one person. Lower-casing
/// the search term is safe against a directory because LDAP matches <c>mail</c> with
/// caseIgnoreIA5Match.
///
/// The validity rule is deliberately identical to <c>[EmailAddress]</c>
/// (System.ComponentModel.DataAnnotations): exactly one '@', neither first nor last character.
/// Keeping the two in step means anything the API layer accepts, the domain also accepts —
/// a stricter domain rule would turn a 400 into a surprise failure deeper in the stack.
/// </summary>
public sealed class Email : IEquatable<Email>
{
    private Email(string value, int atIndex)
    {
        Value = value;
        LocalPart = value[..atIndex];
        DomainPart = value[(atIndex + 1)..];
    }

    /// <summary>The full normalised address, e.g. "ivan.ivanov@example.com".</summary>
    public string Value { get; }

    /// <summary>Everything before the '@' — the conventional source for uid/sAMAccountName.</summary>
    public string LocalPart { get; }

    /// <summary>Everything after the '@'.</summary>
    public string DomainPart { get; }

    /// <exception cref="DomainValidationException">The value is missing or not a well-formed address.</exception>
    public static Email Create(string? value)
    {
        if (!TryCreate(value, out var email))
            throw new DomainValidationException(DomainMessages.EmailInvalid(value));

        return email!;
    }

    public static bool TryCreate(string? value, out Email? email)
    {
        email = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToLowerInvariant();

        var atIndex = normalized.IndexOf('@');
        if (atIndex <= 0 || atIndex == normalized.Length - 1 || atIndex != normalized.LastIndexOf('@'))
            return false;

        email = new Email(normalized, atIndex);
        return true;
    }

    public bool Equals(Email? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as Email);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;

    public static bool operator ==(Email? left, Email? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Email? left, Email? right) => !(left == right);
}
