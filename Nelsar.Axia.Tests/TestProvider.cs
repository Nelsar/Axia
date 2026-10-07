using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Builds a valid <see cref="Provider"/> with everything a test does not care about defaulted.
///
/// <see cref="Provider.Create"/> takes eleven arguments because a provider genuinely has eleven
/// things that must be right; a test about failover hosts should still not have to state a search
/// filter to say so. Named defaults here keep each test to the one field it is about.
///
/// Security defaults to None, the one mode port 389 is valid for without saying more; a test
/// about connection security names its mode and port.
/// </summary>
internal static class TestProvider
{
    public static Provider Create(
        string name = "main",
        string host = "ldap.example.com",
        int port = 389,
        LdapSecurity security = LdapSecurity.None,
        string providerType = "StandardLdap",
        bool isActive = true,
        string? failoverHosts = null,
        string baseDn = "",
        string adminUsername = "",
        string encryptedAdminPassword = "",
        string? userSearchFilter = null,
        int userSearchPageSize = 500) =>
        Provider.Create(
            name,
            host,
            LdapPort.Create(port),
            security,
            providerType,
            isActive,
            failoverHosts,
            baseDn,
            adminUsername,
            EncryptedSecret.FromCipherText(encryptedAdminPassword),
            userSearchFilter is null ? LdapSearchFilter.Default : LdapSearchFilter.Create(userSearchFilter),
            userSearchPageSize);
}
