using System.Globalization;
using Nelsar.Axia.Domain.Resources;
using Nelsar.Axia.Domain.Exceptions;

namespace Nelsar.Axia.Domain.ValueObjects;

/// <summary>
/// A TCP port a directory can actually be reached on.
///
/// The invariant lives here rather than in a request DTO because <c>[Required]</c> on a
/// non-nullable int is a no-op — it accepted 0, -5 and 999999 alike, and those values went
/// straight into the entity and on into <c>LdapConnection.Connect</c>. Holding the rule on the
/// type means every path that builds a Provider has to satisfy it: the API, the seeder, a
/// future import tool, and EF materialising a row.
/// </summary>
public sealed class LdapPort : IEquatable<LdapPort>
{
    public const int Minimum = 1;
    public const int Maximum = 65535;

    /// <summary>The IANA-assigned default for LDAP.</summary>
    public static LdapPort Default { get; } = new(389);

    private LdapPort(int value) => Value = value;

    public int Value { get; }

    /// <exception cref="DomainValidationException">The value is outside the valid port range.</exception>
    public static LdapPort Create(int value)
    {
        if (!TryCreate(value, out var port))
            throw new DomainValidationException(
                DomainMessages.LdapPortOutOfRange(value, Minimum, Maximum));

        return port!;
    }

    public static bool TryCreate(int value, out LdapPort? port)
    {
        port = value is >= Minimum and <= Maximum ? new LdapPort(value) : null;
        return port is not null;
    }

    public bool Equals(LdapPort? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as LdapPort);

    public override int GetHashCode() => Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static bool operator ==(LdapPort? left, LdapPort? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(LdapPort? left, LdapPort? right) => !(left == right);
}
