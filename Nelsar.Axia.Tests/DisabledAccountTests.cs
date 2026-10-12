using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Identity.Providers;
using Novell.Directory.Ldap;

namespace Nelsar.Axia.Tests;

/// <summary>
/// An account switched off in the directory, after it signed in.
///
/// A session is a bearer token valid until it expires, an hour by default, and disabling the account
/// used to change nothing about it: permissions were re-read from the entry on every request, but
/// nothing looked at whether the entry was still allowed to be used. The session now ends on the
/// next request once the cached assessment lapses — while the same entry stays reachable as the
/// target of someone else's request, which is how it gets deleted or switched back on.
/// </summary>
public class DisabledAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static LdapAttributeSet Attributes(params (string Name, string Value)[] values)
    {
        var set = new LdapAttributeSet();
        foreach (var (name, value) in values)
            set.Add(new LdapAttribute(name, value));
        return set;
    }

    private static string FileTime(DateTimeOffset at) => at.UtcDateTime.ToFileTimeUtc().ToString();

    [Theory]
    [InlineData("514", true)]    // NORMAL_ACCOUNT | ACCOUNTDISABLE
    [InlineData("66050", true)]  // ... | DONT_EXPIRE_PASSWORD, still disabled
    [InlineData("512", false)]   // NORMAL_ACCOUNT
    [InlineData("66048", false)] // NORMAL_ACCOUNT | DONT_EXPIRE_PASSWORD
    public void ActiveDirectory_TheDisabledBit(string userAccountControl, bool switchedOff)
    {
        Assert.Equal(switchedOff, StandardLdapProvider.IsSwitchedOff(
            Attributes(("userAccountControl", userAccountControl)), Now));
    }

    [Fact]
    public void ActiveDirectory_AnExpiryInThePast_SwitchesTheAccountOff()
    {
        Assert.True(StandardLdapProvider.IsSwitchedOff(
            Attributes(("accountExpires", FileTime(Now.AddMinutes(-1)))), Now));
        Assert.False(StandardLdapProvider.IsSwitchedOff(
            Attributes(("accountExpires", FileTime(Now.AddDays(1)))), Now));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9223372036854775807")]
    public void ActiveDirectory_BothSpellingsOfNever_AreNotAnExpiry(string accountExpires)
    {
        Assert.False(StandardLdapProvider.IsSwitchedOff(Attributes(("accountExpires", accountExpires)), Now));
    }

    [Fact]
    public void OpenLdap_AnAdministratorsLock_SwitchesTheAccountOff()
    {
        Assert.True(StandardLdapProvider.IsSwitchedOff(
            Attributes(("pwdAccountLockedTime", "000001010000Z")), Now));
    }

    [Fact]
    public void OpenLdap_ALockoutAfterFailedSignIns_DoesNot()
    {
        // Anyone can cause this one by mistyping a password often enough. Counting it would let
        // them end the owner's session at will; it still stops a new sign-in, at the bind.
        Assert.False(StandardLdapProvider.IsSwitchedOff(
            Attributes(("pwdAccountLockedTime", "20261012115500Z")), Now));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    public void EDirectory_LoginDisabled(string loginDisabled, bool switchedOff)
    {
        Assert.Equal(switchedOff, StandardLdapProvider.IsSwitchedOff(
            Attributes(("loginDisabled", loginDisabled)), Now));
    }

    [Fact]
    public void AnEntryThatSaysNothing_IsInUse()
    {
        Assert.False(StandardLdapProvider.IsSwitchedOff(Attributes(("cn", "Ivan Petrov")), Now));
    }

    private static PermissionResolver ResolverFor(LdapUserInfo entry)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserByDnAsync(entry.Dn)).ReturnsAsync(entry);

        var roles = new Mock<IRoleMappingResolver>();
        roles.Setup(x => x.ResolveRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new HashSet<string> { UserRoles.Admin });

        var grants = new Mock<IRolePermissionRepository>();
        grants.Setup(x => x.GetPermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Permissions.All.ToHashSet());

        return new PermissionResolver(ldap.Object, roles.Object, grants.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DirectoryProtectionOptions()));
    }

    private static readonly LdapUserInfo DisabledAdministrator = new()
    {
        Dn = "uid=ivan.petrov,ou=people,dc=example,dc=org",
        Username = "ivan.petrov",
        IsDisabled = true
    };

    [Fact]
    public async Task ASessionWhoseAccountWasSwitchedOff_IsRefused_WhateverItsGroupsGrant()
    {
        var resolver = ResolverFor(DisabledAdministrator);

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => resolver.ResolveAsync(DisabledAdministrator.Dn, providerId: null));
    }

    [Fact]
    public async Task TheSameAccount_AsSomeoneElsesTarget_IsStillAssessed()
    {
        // Assessing it is what lets an administrator act on it — delete it, or switch it back on —
        // and what still keeps everyone else from doing so: it maps to Admin.
        var assessment = await ResolverFor(DisabledAdministrator).AssessEntryAsync(DisabledAdministrator.Dn, providerId: null);

        Assert.NotNull(assessment);
        Assert.True(assessment.IsDisabled);
        Assert.Equal(Permissions.All.Count, assessment.Permissions.Count);
    }

    private static ApiTestFactory FactoryFor(bool disabled)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserByDnAsync(It.IsAny<string>()))
            .ReturnsAsync((string dn) => new LdapUserInfo { Dn = dn, IsDisabled = disabled });

        return new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver, PermissionResolver>();
        });
    }

    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    public async Task ATokenStillInDate_ForAnAccountSwitchedOff_IsAnsweredWith401(bool disabled, HttpStatusCode expected)
    {
        using var factory = FactoryFor(disabled);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.GetAsync("/api/v1/me/permissions");

        Assert.Equal(expected, response.StatusCode);
    }
}
