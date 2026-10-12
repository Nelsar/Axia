using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// What makes a set of <see cref="LdapSettings"/> impossible to connect with — checked when the
/// host starts for the <c>Ldap</c> section, and when a request names an <c>Ldap:Providers:{id}</c>
/// section.
///
/// The case it exists for is the upgrade. Security is LDAPS unless said otherwise, and an
/// appsettings.json written for an earlier version says <c>"Port": 389</c> and nothing about
/// security. Left alone, that instance would start and then fail every sign-in with a TLS
/// handshake error against a server speaking plain LDAP — nowhere near the setting that caused it.
/// </summary>
internal static class LdapSettingsRules
{
    /// <summary>A description of what is wrong, naming keys under <paramref name="section"/>, or null.</summary>
    public static string? Problem(LdapSettings settings, string section)
    {
        // The CA file is shared by every directory — database rows use the host's — so it is
        // checked whether or not this section names a server.
        if (!string.IsNullOrWhiteSpace(settings.TrustedCaCertificatePath))
        {
            try
            {
                TrustedCertificateAuthorities.Load(settings.TrustedCaCertificatePath);
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }

        // Shared by every directory as well, so checked before the early return below. Zero is
        // refused rather than read as "no limit": no limit is exactly what these exist to prevent.
        foreach (var (key, value) in new[]
                 {
                     (nameof(LdapSettings.ConnectTimeoutSeconds), settings.ConnectTimeoutSeconds),
                     (nameof(LdapSettings.OperationTimeoutSeconds), settings.OperationTimeoutSeconds)
                 })
        {
            if (value is < LdapSettings.MinimumTimeoutSeconds or > LdapSettings.MaximumTimeoutSeconds)
            {
                return $"{section}:{key} is {value}; it must be between {LdapSettings.MinimumTimeoutSeconds} "
                       + $"and {LdapSettings.MaximumTimeoutSeconds} seconds.";
            }
        }

        // No server, no default directory to connect to: nothing below can go wrong yet.
        if (string.IsNullOrWhiteSpace(settings.Server))
            return null;

        if (settings.Port is { } port && LdapSecurityPorts.Contradicts(settings.Security, port))
        {
            var standard = LdapSecurityPorts.StandardPortFor(settings.Security);
            return settings.Security == LdapSecurity.Ldaps
                ? $"{section}:Security is Ldaps — the default when it is not set — and {section}:Port is {port}, "
                  + $"which serves plain LDAP. Use port {standard} for LDAPS; or set {section}:Security to StartTls "
                  + $"to upgrade the connection on {port}; or to None to keep plain LDAP, which sends every "
                  + "password to the directory unencrypted."
                : $"{section}:Security is {settings.Security} and {section}:Port is {port}, which expects TLS from the "
                  + $"first byte. Use port {standard}, or set {section}:Security to Ldaps.";
        }

        if (IsActiveDirectory(settings) && settings.Security == LdapSecurity.StartTls)
        {
            return $"{section}:ProviderType is ActiveDirectory, which cannot use StartTls: "
                   + "System.DirectoryServices has no StartTLS. Use Ldaps, or ProviderType StandardLdap.";
        }

        return null;
    }

    public static bool IsActiveDirectory(LdapSettings settings) =>
        settings.ProviderType.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Runs <see cref="LdapSettingsRules"/> over the <c>Ldap</c> section at startup.</summary>
internal sealed class LdapSettingsValidation : IValidateOptions<LdapSettings>
{
    public ValidateOptionsResult Validate(string? name, LdapSettings options) =>
        LdapSettingsRules.Problem(options, "Ldap") is { } problem
            ? ValidateOptionsResult.Fail(problem)
            : ValidateOptionsResult.Success;
}
