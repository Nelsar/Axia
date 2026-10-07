using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The rules that used to live at whichever call site happened to remember them, now held by the
/// aggregates themselves. Each of these was reachable before: the entities were bags of public
/// setters, so a seeder, a repository copy loop or a second handler could write anything.
/// </summary>
public class ProviderInvariantTests
{
    [Fact]
    public void Create_IssuesAnIdentity_SoTheAggregateIsNeverWithoutOne()
    {
        Assert.NotEqual(Guid.Empty, TestProvider.Create().Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AProviderCannotBeLeftWithoutAName(string? name)
    {
        Assert.Throws<DomainValidationException>(() => TestProvider.Create(name: name!));
        Assert.Throws<DomainValidationException>(() => TestProvider.Create().Rename(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void AProviderCannotBePointedAtNoHost(string? host)
    {
        Assert.Throws<DomainValidationException>(
            () => TestProvider.Create().PointAt(host, LdapPort.Default, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20_001)]
    public void AnUnusableSearchPageSizeIsRefused(int pageSize)
    {
        // Zero and negative reached the paging control, where they mean "no paging" on some
        // servers and are an error on others.
        Assert.Throws<DomainValidationException>(
            () => TestProvider.Create().ConfigureUserSearch(LdapSearchFilter.Default, pageSize));
    }

    [Fact]
    public void BaseDnAndAdminUsername_MayBeBlank_BecauseThatIsARealConfiguration()
    {
        // Blank admin username is an anonymous bind; blank base DN falls back to configuration.
        // Both are what the request contract has always accepted.
        var provider = TestProvider.Create(baseDn: "", adminUsername: "");

        Assert.Equal(string.Empty, provider.BaseDn);
        Assert.Equal(string.Empty, provider.AdminUsername);
    }

    [Fact]
    public void ActivateAndDeactivate_ReplaceAssigningTheFlag()
    {
        var provider = TestProvider.Create(isActive: false);
        Assert.False(provider.IsActive);

        provider.Activate();
        Assert.True(provider.IsActive);

        provider.Deactivate();
        Assert.False(provider.IsActive);
    }

    [Fact]
    public void NotRotatingTheCredential_LeavesTheStoredOne()
    {
        var provider = TestProvider.Create(encryptedAdminPassword: "enc:old");

        provider.Rename("renamed");

        Assert.Equal("enc:old", provider.EncryptedAdminPassword.Reveal());
    }

    [Fact]
    public void AValueLongerThanItsColumn_FailsHere_RatherThanAtSaveChanges()
    {
        // Previously surfaced as a provider-specific truncation error naming no property.
        Assert.Throws<DomainValidationException>(
            () => TestProvider.Create(name: new string('x', Provider.MaximumNameLength + 1)));
    }
}

public class RoleMappingInvariantTests
{
    [Theory]
    [InlineData(null, "Admin")]
    [InlineData("", "Admin")]
    [InlineData("CN=Administrators,OU=Groups,DC=example,DC=com", null)]
    [InlineData("CN=Administrators,OU=Groups,DC=example,DC=com", "  ")]
    public void AMappingThatGrantsOrMatchesNothing_IsRefused(string? group, string? role)
    {
        // Both failures are silent at sign-in: the user simply lacks permissions they should have.
        Assert.Throws<DomainValidationException>(() => RoleMapping.Create(null, group, role, 0));
    }

    [Fact]
    public void ANegativePriority_IsRefused_BecauseItWouldOutrankTheAdminDefault()
    {
        Assert.Throws<DomainValidationException>(
            () => RoleMapping.Create(null, "CN=Administrators,OU=Groups,DC=example,DC=com", "Admin", -1));
    }

    [Theory]
    [InlineData("Administrators")]
    [InlineData("Domain Admins")]
    public void AGroupGivenByNameAlone_IsRefused_BecauseItWouldMatchEveryGroupOfThatName(string group)
    {
        Assert.Throws<DomainValidationException>(() => RoleMapping.Create(null, group, "Admin", 0));
    }

    [Fact]
    public void AGroupDn_IsStoredWithoutTheSpacingItWasTypedWith()
    {
        var mapping = RoleMapping.Create(null, " CN=Administrators, OU=Groups ,DC=example,DC=com ", "Admin", 0);

        Assert.Equal("CN=Administrators,OU=Groups,DC=example,DC=com", mapping.GroupName);
        Assert.False(mapping.IdentifiesGroupByNameOnly);
    }

    [Fact]
    public void TheSeedersMappingByName_IsKept_AndSaysWhatItIs()
    {
        var mapping = RoleMapping.SeedByGroupName(null, "Administrators", "Admin", 0);

        Assert.Equal("Administrators", mapping.GroupName);
        Assert.True(mapping.IdentifiesGroupByNameOnly);
    }

    [Fact]
    public void EditingAMappingStillWrittenByName_RequiresADn()
    {
        var mapping = RoleMapping.SeedByGroupName(null, "Administrators", "Admin", 0);

        Assert.Throws<DomainValidationException>(() => mapping.MapGroup("Administrators"));
        mapping.MapGroup("CN=Administrators,CN=Builtin,DC=example,DC=com");
        Assert.False(mapping.IdentifiesGroupByNameOnly);
    }

    [Fact]
    public void ScopeTo_MovesAMappingBetweenGlobalAndOneDomain()
    {
        var providerId = Guid.NewGuid();
        var mapping = RoleMapping.Create(null, "CN=Administrators,OU=Groups,DC=example,DC=com", "Admin", 0);

        mapping.ScopeTo(providerId);
        Assert.Equal(providerId, mapping.ProviderId);

        mapping.ScopeTo(null);
        Assert.Null(mapping.ProviderId);
    }
}

public class RateLimitSettingsInvariantTests
{
    [Theory]
    [InlineData(0, 60, 0)]
    [InlineData(-1, 60, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(5, -1, 0)]
    [InlineData(5, 60, -1)]
    public void LimitsThatWouldBreakThePolicy_AreRefused(int permit, int window, int queue)
    {
        // A permit limit of zero wedges the login endpoint shut for everyone — a denial of
        // service configured by hand. Only the HTTP validator used to catch it.
        Assert.Throws<DomainValidationException>(
            () => RateLimitSettings.Create("auth-login", permit, window, queue, true));
    }

    [Fact]
    public void ANamelessPolicy_IsRefused()
    {
        Assert.Throws<DomainValidationException>(() => RateLimitSettings.Create("  ", 5, 60, 0, true));
    }

    [Fact]
    public void Disable_TurnsThePolicyOff_AsItsOwnNamedOperation()
    {
        var settings = RateLimitSettings.Create("auth-login", 5, 60, 0, true);

        settings.Disable();
        Assert.False(settings.Enabled);

        settings.Enable();
        Assert.True(settings.Enabled);
    }
}

public class RolePermissionInvariantTests
{
    [Fact]
    public void Grant_AcceptsAPermissionFromTheCatalogue()
    {
        var grant = RolePermission.Grant("Admin", Permissions.UsersDelete);

        Assert.Equal(Permissions.UsersDelete, grant.Permission);
        Assert.NotEqual(Guid.Empty, grant.Id);
    }

    [Fact]
    public void Grant_RefusesAPermissionNothingEnforces()
    {
        // Otherwise it is a row an administrator can see, believe in, and that grants nothing.
        var failure = Assert.Throws<DomainValidationException>(
            () => RolePermission.Grant("Admin", "users.approve"));

        Assert.Contains("users.approve", failure.Message);
    }

    [Fact]
    public void Grant_RefusesABlankRole()
    {
        Assert.Throws<DomainValidationException>(() => RolePermission.Grant(" ", Permissions.UsersRead));
    }
}

public class AuditEventInvariantTests
{
    [Fact]
    public void AnOverlongDetail_IsTruncated_RatherThanLosingTheEntry()
    {
        // The audit row is written on the failure path of other operations; an exception thrown
        // by the write itself loses the very event worth keeping.
        var evt = AuditEvent.Modification(
            null, null, "tester", "Create", false, new string('x', 10_000), DateTimeOffset.UtcNow);

        Assert.Equal(AuditEvent.MaximumDetailsLength, evt.Details!.Length);
    }

    [Fact]
    public void AnEventWithNoActor_StillNamesSomething()
    {
        var evt = AuditEvent.Authentication(null, false, null, null, null, DateTimeOffset.UtcNow);

        Assert.False(string.IsNullOrWhiteSpace(evt.Actor));
    }

    [Fact]
    public void TheTimestampIsWhenItHappened_NotWhenTheWriteRan()
    {
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-5);

        var evt = AuditEvent.Authentication("tester", true, null, null, null, occurred);

        Assert.Equal(occurred, evt.Timestamp);
    }
}
