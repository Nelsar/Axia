using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Tests;

/// <summary>
/// What has to hold before the application meets a real directory: passwords written the way
/// the directory hashes them, secrets kept out of the attributes view, privileged entries
/// recognised as such, and role mappings that start out naming groups by DN.
/// </summary>
public class DeploymentHardeningTests
{
    // --- Passwords ---------------------------------------------------------------------------

    [Fact]
    public void ThePasswordModifyRequest_IsTheRfc3062Sequence()
    {
        var encoded = DirectoryPasswords.PasswordModifyRequest("uid=a,dc=x", "pw");

        // SEQUENCE { [0] "uid=a,dc=x", [2] "pw" }
        var expected = new byte[] { 0x30, 0x10, 0x80, 0x0A }
            .Concat(Encoding.UTF8.GetBytes("uid=a,dc=x"))
            .Concat(new byte[] { 0x82, 0x02 })
            .Concat(Encoding.UTF8.GetBytes("pw"))
            .ToArray();
        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void ThePasswordModifyRequest_UsesLongFormLengths_PastOneHundredAndTwentySevenBytes()
    {
        var dn = "uid=" + new string('x', 200) + ",dc=example,dc=com";
        var encoded = DirectoryPasswords.PasswordModifyRequest(dn, "pw");

        // A DN of 128..255 bytes: tag 0x80, then 0x81 — one length byte follows — then the length.
        var dnLength = Encoding.UTF8.GetByteCount(dn);
        Assert.InRange(dnLength, 128, 255);
        Assert.Equal(new byte[] { 0x80, 0x81, (byte)dnLength }, encoded[3..6]);

        // And the sequence around it, long form too.
        Assert.Equal(new byte[] { 0x30, 0x81, (byte)(encoded.Length - 3) }, encoded[..3]);
    }

    [Fact]
    public void TheActiveDirectoryPassword_IsQuotedUtf16LittleEndian()
    {
        Assert.Equal(Encoding.Unicode.GetBytes("\"Pa55-word!\""), DirectoryPasswords.ActiveDirectoryValue("Pa55-word!"));
    }

    // --- The attributes view -----------------------------------------------------------------

    [Theory]
    [InlineData("ms-Mcs-AdmPwd")]
    [InlineData("msLAPS-Password")]
    [InlineData("msLAPS-PasswordExpirationTime")]
    [InlineData("msFVE-RecoveryPassword")]
    [InlineData("authPassword")]
    [InlineData("unixUserPassword")]
    [InlineData("ipaNTHash")]
    [InlineData("msPKIAccountCredentials")]
    [InlineData("someVendorSecretToken")]
    public void SecretsAreWithheld_ByNameOrByWhatTheNameSays(string attribute)
    {
        Assert.True(DirectoryAttributeReader.IsSecret(attribute));
    }

    [Theory]
    [InlineData("cn")]
    [InlineData("mail")]
    [InlineData("memberOf")]
    [InlineData("userAccountControl")]
    public void OrdinaryAttributes_AreStillShown(string attribute)
    {
        Assert.False(DirectoryAttributeReader.IsSecret(attribute));
    }

    // --- Privileged entries ------------------------------------------------------------------

    private static PermissionResolver ResolverFor(LdapUserInfo entry, params string[] protectedGroups)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserByDnAsync(entry.Dn)).ReturnsAsync(entry);

        var roles = new Mock<IRoleMappingResolver>();
        roles.Setup(x => x.ResolveRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new HashSet<string> { UserRoles.User });

        var grants = new Mock<IRolePermissionRepository>();
        grants.Setup(x => x.GetPermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { Permissions.UsersRead });

        return new PermissionResolver(ldap.Object, roles.Object, grants.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DirectoryProtectionOptions { ProtectedGroups = protectedGroups }));
    }

    [Fact]
    public async Task AnEntryTheDirectoryMarksPrivileged_IsProtected_WhateverItMapsToHere()
    {
        var entry = new LdapUserInfo { Dn = "CN=da,CN=Users,DC=x", IsPrivileged = true, Groups = ["CN=Domain Admins,CN=Users,DC=x"] };

        var assessment = await ResolverFor(entry).AssessEntryAsync(entry.Dn, null);

        Assert.NotNull(assessment);
        Assert.True(assessment.IsProtected);
        Assert.Equal(new[] { Permissions.UsersRead }, assessment.Permissions);
    }

    [Fact]
    public async Task MembershipOfAConfiguredGroup_IsProtected_ForDirectoriesThatMarkNothing()
    {
        var entry = new LdapUserInfo { Dn = "uid=root,ou=people,dc=x", Groups = ["cn=ldap-admins,ou=groups,dc=x"] };

        var assessment = await ResolverFor(entry, "CN=LDAP-Admins, OU=Groups, DC=x").AssessEntryAsync(entry.Dn, null);

        Assert.True(assessment!.IsProtected);
    }

    [Fact]
    public async Task TheServiceAccount_IsSaidToBeOne()
    {
        var entry = new LdapUserInfo { Dn = "cn=svc,dc=x", IsServiceAccount = true };

        var assessment = await ResolverFor(entry).AssessEntryAsync(entry.Dn, null);

        Assert.True(assessment!.IsServiceAccount);
        Assert.False(assessment.IsProtected);
    }

    // --- Role mappings at deployment -----------------------------------------------------------

    private static AppDbContext Database() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RoleMappingOptions MappingOptions(bool allowByName = false, params (string Role, string Dn)[] groups)
    {
        var options = new RoleMappingOptions { AllowMappingsByName = allowByName };
        foreach (var (role, dn) in groups)
            options.GroupDns[role] = dn;
        return options;
    }

    [Fact]
    public async Task AFreshDatabase_IsSeededFromGroupDns_ByDistinguishedName()
    {
        await using var db = Database();
        var options = MappingOptions(groups: [("Admin", "CN=Axia-Admins,OU=Groups,DC=x"), ("usereditor", "CN=Axia-Editors,OU=Groups,DC=x")]);

        await DbInitializer.SeedRoleMappingsAsync(db, NullLogger.Instance, options);
        await DbInitializer.EnsureAnAdministratorIsMappedAsync(db, options);

        var mappings = await db.RoleMappings.ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, m => Assert.False(m.IdentifiesGroupByNameOnly));
        Assert.Contains(mappings, m => m.RoleName == UserRoles.Admin && m.GroupName == "CN=Axia-Admins,OU=Groups,DC=x");
        Assert.Contains(mappings, m => m.RoleName == UserRoles.UserEditor);
    }

    [Fact]
    public async Task WithNoAdministratorGroupGiven_TheApplicationRefusesToStart_RatherThanGuess()
    {
        await using var db = Database();
        var options = MappingOptions();

        await DbInitializer.SeedRoleMappingsAsync(db, NullLogger.Instance, options);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DbInitializer.EnsureAnAdministratorIsMappedAsync(db, options));
        Assert.Contains("RoleMappings:GroupDns:Admin", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await db.RoleMappings.ToListAsync());
    }

    [Fact]
    public async Task AnUpgradedDatabase_HasItsMappingsByNameConvertedToTheConfiguredDns()
    {
        await using var db = Database();
        db.RoleMappings.AddRange(
            RoleMapping.SeedByGroupName(null, "Administrators", UserRoles.Admin, 0),
            RoleMapping.SeedByGroupName(null, "UserDeleters", UserRoles.UserDeleter, 10),
            RoleMapping.SeedByGroupName(Guid.NewGuid(), "Administrators", UserRoles.Admin, 0));
        await db.SaveChangesAsync();
        var options = MappingOptions(groups: [("Admin", "CN=Axia-Admins,OU=Groups,DC=x")]);

        await DbInitializer.SeedRoleMappingsAsync(db, NullLogger.Instance, options);
        await DbInitializer.EnsureAnAdministratorIsMappedAsync(db, options);

        var mappings = await db.RoleMappings.ToListAsync();
        Assert.Contains(mappings, m => m.ProviderId == null && m.RoleName == UserRoles.Admin && m.GroupName == "CN=Axia-Admins,OU=Groups,DC=x");

        // No DN was given for UserDeleter, and a row scoped to another directory is not about the
        // group a default-directory DN names: both are left as they were, to be reported.
        Assert.Contains(mappings, m => m.RoleName == UserRoles.UserDeleter && m.IdentifiesGroupByNameOnly);
        Assert.Contains(mappings, m => m.ProviderId != null && m.IdentifiesGroupByNameOnly);
    }

    [Fact]
    public async Task TheOldByNameDefaults_AreSeededOnlyWhereTheDeploymentAsksForThem()
    {
        await using var db = Database();
        var options = MappingOptions(allowByName: true);

        await DbInitializer.SeedRoleMappingsAsync(db, NullLogger.Instance, options);
        await DbInitializer.EnsureAnAdministratorIsMappedAsync(db, options);

        Assert.Contains(await db.RoleMappings.ToListAsync(), m => m.RoleName == UserRoles.Admin && m.IdentifiesGroupByNameOnly);
    }

    [Theory]
    [InlineData("Auditor", "CN=Auditors,DC=x")]
    [InlineData("Admin", "Administrators")]
    public void ABadGroupDnsEntry_StopsTheHost_WithTheSettingNamed(string role, string dn)
    {
        var result = new RoleMappingOptionsValidation().Validate(null, MappingOptions(groups: [(role, dn)]));

        Assert.True(result.Failed);
        Assert.Contains("RoleMappings:GroupDns", result.FailureMessage, StringComparison.Ordinal);
    }
}
