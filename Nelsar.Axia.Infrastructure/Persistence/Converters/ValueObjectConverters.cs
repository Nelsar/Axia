using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps <see cref="LdapPort"/> onto the INTEGER column it has always been stored in. Reading
/// goes through <see cref="LdapPort.Create"/>, so the port-range invariant holds for rows that
/// arrived before it existed as much as for ones written after.
/// </summary>
internal sealed class LdapPortConverter : ValueConverter<LdapPort, int>
{
    public LdapPortConverter()
        : base(port => port.Value, value => LdapPort.Create(value))
    {
    }
}

/// <summary>
/// Stores the bind credential in the TEXT column it has always occupied. The ciphertext is what
/// goes to the database — <see cref="EncryptedSecret.ToString"/> would write the mask, which is
/// exactly the accident the type exists to prevent everywhere <em>except</em> here, so the
/// conversion is explicit about calling <see cref="EncryptedSecret.Reveal"/>.
/// </summary>
internal sealed class EncryptedSecretConverter : ValueConverter<EncryptedSecret, string>
{
    public EncryptedSecretConverter()
        : base(secret => secret.Reveal(), value => EncryptedSecret.FromCipherText(value))
    {
    }
}

/// <summary>
/// Maps <see cref="LdapSearchFilter"/> onto its existing TEXT column. Reading goes through
/// <see cref="LdapSearchFilter.FromStorage"/> rather than Create: see the remarks there for why a
/// stored filter is not re-validated on the way out.
/// </summary>
internal sealed class LdapSearchFilterConverter : ValueConverter<LdapSearchFilter, string>
{
    public LdapSearchFilterConverter()
        : base(filter => filter.Template, value => LdapSearchFilter.FromStorage(value))
    {
    }
}
