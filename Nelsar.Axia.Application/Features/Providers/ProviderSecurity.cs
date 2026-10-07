using FluentValidation;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Providers;

/// <summary>
/// The security mode as it arrives on a provider request: a name, or nothing.
///
/// What "nothing" means differs between the two commands, and on purpose. Creating a provider
/// without one gets LDAPS. Updating without one keeps whatever the provider already has —
/// otherwise a console from before this field existed would switch every directory it saved to
/// LDAPS, and one that only ever spoke plain LDAP would stop answering.
/// </summary>
internal static class ProviderSecurity
{
    /// <exception cref="DomainValidationException">The value names no mode.</exception>
    public static LdapSecurity Parse(string? value, LdapSecurity whenOmitted)
    {
        if (value is null)
            return whenOmitted;

        return LdapSecurityPorts.TryParse(value, out var security)
            ? security
            : throw new DomainValidationException(ApplicationMessages.LdapSecurityUnknown(value));
    }

    /// <summary>The validator rule both commands share: absent, or the name of a mode.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeASecurityMode<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is null || LdapSecurityPorts.TryParse(value, out _))
            .WithMessage((_, value) => ApplicationMessages.LdapSecurityUnknown(value));
}
