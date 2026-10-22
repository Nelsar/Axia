using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Providers;
using Novell.Directory.Ldap;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A test that needs the Samba AD domain controller in deploy/dev/samba.
/// <c>AXIA_TEST_SAMBA</c> names it as host:port of its LDAPS listener, and
/// <c>AXIA_TEST_SAMBA_CA</c> the CA it writes to /export/ca.crt.
///
/// Skipped without them, as <see cref="PostgreSqlFactAttribute"/> is; CI's API job starts one and
/// sets <c>AXIA_TEST_SAMBA_REQUIRED</c>, which turns a missing directory into failures.
/// </summary>
public sealed class SambaFactAttribute : FactAttribute
{
    public const string Server = "AXIA_TEST_SAMBA";
    public const string CertificateAuthority = "AXIA_TEST_SAMBA_CA";

    public SambaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Server))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Server + "_REQUIRED")))
        {
            Skip = $"Set {Server} to the Samba DC's host:port (LDAPS) and {CertificateAuthority} to its CA; see deploy/dev/samba.";
        }
    }
}

/// <summary>
/// The provider against a real Active Directory implementation, Samba's — the first tests here
/// against a live directory rather than one faked at the provider boundary. What they pin is what
/// a fake cannot say: that an account created through the API is an account the directory and
/// Windows will treat as one, that a group is a security group, and that a directory which never
/// sets adminCount still has its administrators protected.
///
/// Every test makes what it changes under a name of its own and removes it afterwards; the
/// provisioned accounts (deploy/dev/samba/provision.sh) are only read.
/// </summary>
public class SambaAdTests
{
    private const string BaseDn = "DC=axia,DC=test";
    private const string UsersDn = "CN=Users," + BaseDn;
    private const string DemoPassword = "Axia-Demo-1";

    private static LdapSettings Settings()
    {
        var server = Environment.GetEnvironmentVariable(SambaFactAttribute.Server)
            ?? throw new InvalidOperationException($"{SambaFactAttribute.Server} is not set.");
        var (host, port) = server.LastIndexOf(':') is var colon and > 0
            ? (server[..colon], int.Parse(server[(colon + 1)..]))
            : (server, 636);

        return new LdapSettings
        {
            Server = host,
            Port = port,
            Security = LdapSecurity.Ldaps,
            TrustedCaCertificatePath = Environment.GetEnvironmentVariable(SambaFactAttribute.CertificateAuthority),
            BaseDn = BaseDn,
            AdminUsername = "Administrator@axia.test",
            AdminPassword = "Axia-Admin-1",
        };
    }

    private static StandardLdapProvider Provider()
    {
        var options = Options.Create(Settings());
        return new StandardLdapProvider(options, new LdapConnectionFactory(options), NullLogger<StandardLdapProvider>.Instance);
    }

    /// <summary>The directory as the bind account sees it, for what the provider has no call for.</summary>
    private static LdapConnection Admin() => new LdapConnectionFactory(Options.Create(Settings())).CreateAdminConnection();

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 8)];

    private static LdapAttributeSet Read(string dn, params string[] attributes)
    {
        using var conn = Admin();
        return conn.Read(dn, attributes).GetAttributeSet();
    }

    private static string Value(LdapAttributeSet attrs, string name) =>
        attrs.TryGetValue(name, out var attribute) ? attribute.StringValue ?? string.Empty : string.Empty;

    private static void DeleteIfThere(string dn)
    {
        try
        {
            using var conn = Admin();
            conn.Delete(dn);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // Intentionally ignored: cleanup is idempotent and the entry may already be absent.
        }
    }

    private static void AddMember(string groupDn, string memberDn)
    {
        using var conn = Admin();
        conn.Modify(groupDn, new LdapModification(LdapModification.Add, new LdapAttribute("member", memberDn)));
    }

    [SambaFact]
    public void ADemoAccount_SignsIn_AndAWrongPasswordDoesNot()
    {
        var provider = Provider();

        Assert.Equal("CN=admin demo," + UsersDn, provider.Authenticate("admin.demo", DemoPassword), ignoreCase: true);
        Assert.Null(provider.Authenticate("admin.demo", "not-the-password"));
    }

    [SambaFact]
    public void AnAdministratorAddedAfterProvisioning_IsPrivileged_ThoughSambaNeverSetAdminCount()
    {
        // The account Samba's missing SDProp leaves unmarked: in Domain Admins, no adminCount.
        const string itAdmin = "CN=it admin," + UsersDn;
        Assert.Equal(string.Empty, Value(Read(itAdmin, "adminCount"), "adminCount"));

        var provider = Provider();

        Assert.True(provider.FindUserByDn(itAdmin)!.IsPrivileged);
        Assert.True(provider.FindUserByDn("CN=Administrator," + UsersDn)!.IsPrivileged);
        Assert.False(provider.FindUserByDn("CN=user demo," + UsersDn)!.IsPrivileged);
    }

    [SambaFact]
    public void ADisabledAccount_IsReportedSo()
    {
        Assert.True(Provider().FindUserByDn("CN=disabled demo," + UsersDn)!.IsDisabled);
        Assert.False(Provider().FindUserByDn("CN=user demo," + UsersDn)!.IsDisabled);
    }

    [SambaFact]
    public void ACreatedUser_IsAnEnabledAccount_ThatSignsInWithItsPassword()
    {
        var login = Unique("t.user");
        var dn = $"CN=Test {login},{BaseDn}";
        var provider = Provider();

        try
        {
            provider.CreateUser($"Test {login}", $"{login}@axia.test", DemoPassword);

            var attrs = Read(dn, "objectClass", "sAMAccountName", "userAccountControl", "mail", "displayName", "objectSid");
            Assert.Contains("user", attrs.GetAttribute("objectClass").StringValueArray, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(login, Value(attrs, "sAMAccountName"));
            Assert.Equal($"{login}@axia.test", Value(attrs, "mail"));
            Assert.True(attrs.TryGetValue("objectSid", out _), "A security principal has a SID.");

            // NORMAL_ACCOUNT, enabled, a password required.
            Assert.Equal(0x200, int.Parse(Value(attrs, "userAccountControl")) & 0x222);

            Assert.Equal(dn, provider.Authenticate(login, DemoPassword), ignoreCase: true);
        }
        finally
        {
            DeleteIfThere(dn);
        }
    }

    [SambaFact]
    public void APasswordThePolicyRefuses_IsTheCallersToFix_AndLeavesNoAccountBehind()
    {
        var login = Unique("t.weak");
        var dn = $"CN=Test {login},{BaseDn}";

        try
        {
            Assert.Throws<DomainValidationException>(() =>
                Provider().CreateUser($"Test {login}", $"{login}@axia.test", "weak"));

            using var conn = Admin();
            var ex = Assert.Throws<LdapException>(() => conn.Read(dn));
            Assert.Equal(LdapException.NoSuchObject, ex.ResultCode);
        }
        finally
        {
            DeleteIfThere(dn);
        }
    }

    [SambaFact]
    public void ALoginActiveDirectoryCannotHold_IsRefusedBeforeAnythingIsWritten()
    {
        // 21 characters: one more than sAMAccountName takes for a user.
        const string login = "t.twentyonecharacter";
        Assert.Equal(21, (login + "s").Length);

        var ex = Assert.Throws<DomainValidationException>(() =>
            Provider().CreateUser("Test too long", $"{login}s@axia.test", DemoPassword));
        Assert.Contains("20", ex.Message);

        using var conn = Admin();
        var lookup = Assert.Throws<LdapException>(() => conn.Read($"CN=Test too long,{BaseDn}"));
        Assert.Equal(LdapException.NoSuchObject, lookup.ResultCode);
    }

    [SambaFact]
    public void AResetPassword_IsTheOneThatSignsIn()
    {
        var login = Unique("t.reset");
        var dn = $"CN=Test {login},{BaseDn}";
        var provider = Provider();

        try
        {
            provider.CreateUser($"Test {login}", $"{login}@axia.test", DemoPassword);
            provider.UpdateUser(dn, null, null, "Axia-Reset-2");

            // Only the new one is asserted: Samba goes on accepting the previous password for
            // "old password allowed period", 60 minutes by default.
            Assert.Equal(dn, provider.Authenticate(login, "Axia-Reset-2"), ignoreCase: true);
            Assert.Null(provider.Authenticate(login, "Axia-Never-3"));
        }
        finally
        {
            DeleteIfThere(dn);
        }
    }

    [SambaFact]
    public void ACreatedGroup_IsASecurityGroup_WithItsMembers()
    {
        var name = Unique("t-group-");
        var provider = Provider();
        string? dn = null;

        try
        {
            dn = provider.CreateGroup(name, UsersDn, "made by a test", ["CN=user demo," + UsersDn]);

            var attrs = Read(dn, "objectClass", "groupType", "sAMAccountName", "objectSid");
            Assert.Contains("group", attrs.GetAttribute("objectClass").StringValueArray, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(name, Value(attrs, "sAMAccountName"));
            Assert.True(attrs.TryGetValue("objectSid", out _), "A security group has a SID.");
            // GROUP_TYPE_SECURITY_ENABLED.
            Assert.NotEqual(0, int.Parse(Value(attrs, "groupType")) & unchecked((int)0x80000000));

            Assert.Contains("CN=user demo," + UsersDn, provider.GetGroupMembers(dn).Dns, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (dn is not null)
                DeleteIfThere(dn);
        }
    }

    [SambaFact]
    public void AGroupNestedInDomainAdmins_IsPrivileged()
    {
        var name = Unique("t-nested-");
        var provider = Provider();
        string? dn = null;

        try
        {
            dn = provider.CreateGroup(name, UsersDn, "", ["CN=user demo," + UsersDn]);
            Assert.False(provider.GetAllGroups().Groups.Single(group => group.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase)).IsPrivileged);

            AddMember("CN=Domain Admins," + UsersDn, dn);

            var groups = provider.GetAllGroups().Groups;
            Assert.True(groups.Single(group => group.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase)).IsPrivileged);
            Assert.True(groups.Single(group => group.Name == "Domain Admins").IsPrivileged);
            Assert.False(groups.Single(group => group.Name == "axia-editors").IsPrivileged);
        }
        finally
        {
            if (dn is not null)
                DeleteIfThere(dn);
        }
    }

    [SambaFact]
    public void AUnitHoldingAnAdministrator_HoldsSomethingPrivileged()
    {
        var provider = Provider();
        var unit = provider.CreateOrganizationalUnit(Unique("t-unit-"), BaseDn, "made by a test");
        var login = Unique("t.admin");
        var userDn = $"CN=Test {login},{BaseDn}";
        var movedDn = $"CN=Test {login},{unit}";

        try
        {
            Assert.False(provider.InspectSubtree(unit).HoldsPrivileged);

            provider.CreateUser($"Test {login}", $"{login}@axia.test", DemoPassword);
            AddMember("CN=Domain Admins," + UsersDn, userDn);
            using (var conn = Admin())
                conn.Rename(userDn, $"CN=Test {login}", unit, true);

            Assert.True(provider.InspectSubtree(unit).HoldsPrivileged);
        }
        finally
        {
            DeleteIfThere(movedDn);
            DeleteIfThere(userDn);
            DeleteIfThere(unit);
        }
    }

    [SambaFact]
    public void TheDomainController_IsAPrivilegedComputer_AndACreatedOneIsNot()
    {
        var name = Unique("TPC");
        var provider = Provider();
        string? dn = null;

        try
        {
            dn = provider.CreateComputer(name, BaseDn, "made by a test", $"{name}.axia.test");

            var computers = provider.GetAllComputers().Computers;
            Assert.True(computers.Single(computer => computer.Name.Equals("DC1", StringComparison.OrdinalIgnoreCase)).IsPrivileged);
            Assert.False(computers.Single(computer => computer.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase)).IsPrivileged);
        }
        finally
        {
            if (dn is not null)
                DeleteIfThere(dn);
        }
    }

    [SambaFact]
    public void Listings_AreComplete_AndHoldTheDemoEntries()
    {
        var provider = Provider();

        var users = provider.GetAllUsers();
        Assert.True(users.IsComplete);
        Assert.Contains(users.Users, user => user.Username == "admin.demo");
        // Computer accounts derive from user in AD; they are not people.
        Assert.DoesNotContain(users.Users, user => user.Username.EndsWith('$'));

        var groups = provider.GetAllGroups();
        Assert.True(groups.IsComplete);
        Assert.Contains(groups.Groups, group => group.Name == "axia-admins");

        Assert.Contains(provider.GetAllOrganizationalUnits().Units,
            unit => unit.Name == "Domain Controllers");
    }
}
