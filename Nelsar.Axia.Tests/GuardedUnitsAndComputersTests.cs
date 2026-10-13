using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Computers;
using Nelsar.Axia.Application.Features.Computers.Commands;
using Nelsar.Axia.Application.Features.OrganizationalUnits;
using Nelsar.Axia.Application.Features.OrganizationalUnits.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Identity.Providers;
using Novell.Directory.Ldap;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Organizational units and computers under the same rule as accounts and groups: nobody acts on
/// something more powerful than they are.
///
/// Renaming or moving a unit re-addresses everything beneath it. A group mapped to a role by DN
/// stops matching, a protected group stops being protected — so orgunits.manage beside
/// users.update was enough to move the unit holding the Admin group, reset an administrator's
/// password while they outranked nobody, and move it back. And computers.manage could disable or
/// delete a domain controller's account.
/// </summary>
public class GuardedUnitsAndComputersTests
{
    private const string CallerDn = "uid=caller,ou=people,dc=example,dc=com";
    private const string GroupsUnitDn = "ou=groups,dc=example,dc=com";
    private const string AdminGroupDn = "cn=Administrators,ou=groups,dc=example,dc=com";
    private const string PlainUnitDn = "ou=projects,dc=example,dc=com";

    private static readonly LdapOrganizationalUnit GroupsUnit = new() { Dn = GroupsUnitDn, Name = "groups", ParentDn = "dc=example,dc=com" };
    private static readonly LdapOrganizationalUnit PlainUnit = new() { Dn = PlainUnitDn, Name = "projects", ParentDn = "dc=example,dc=com" };

    private static readonly LdapComputer DomainController = new()
    {
        Dn = "CN=DC01,OU=Domain Controllers,DC=example,DC=com", Name = "DC01",
        ParentDn = "OU=Domain Controllers,DC=example,DC=com", IsPrivileged = true
    };
    private static readonly LdapComputer Workstation = new()
    {
        Dn = "CN=WS01,OU=Workstations,DC=example,DC=com", Name = "WS01", ParentDn = "OU=Workstations,DC=example,DC=com"
    };
    private static readonly LdapComputer ManagedServiceAccount = new()
    {
        Dn = "CN=gmsa-axia,CN=Managed Service Accounts,DC=example,DC=com", Name = "gmsa-axia",
        ParentDn = "CN=Managed Service Accounts,DC=example,DC=com", IsServiceAccount = true
    };

    /// <summary>Everything an organizational-unit or computer manager might be given, short of Admin.</summary>
    private static readonly IReadOnlySet<string> Manager = new HashSet<string>
    {
        Permissions.UsersRead, Permissions.UsersUpdate,
        Permissions.OrganizationalUnitsRead, Permissions.OrganizationalUnitsManage,
        Permissions.ComputersRead, Permissions.ComputersManage
    };

    private static readonly IReadOnlySet<string> Everything = Permissions.All.ToHashSet();

    private static Guid Id(string dn) => DeterministicIdFactory.BuildDirectoryObjectId(dn);

    private static (ISender Sender, Mock<ILdapService> Ldap) Pipeline(
        IReadOnlySet<string> callerHolds,
        DirectorySubtree? beneath = null,
        params string[] protectedGroups)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetOrganizationalUnitsAsync())
            .ReturnsAsync(DirectoryOrganizationalUnitListing.Complete([GroupsUnit, PlainUnit]));
        ldap.Setup(x => x.GetComputersAsync())
            .ReturnsAsync(DirectoryComputerListing.Complete([DomainController, Workstation, ManagedServiceAccount]));
        ldap.Setup(x => x.InspectSubtreeAsync(It.IsAny<string>()))
            .ReturnsAsync(beneath ?? new DirectorySubtree(HoldsPrivileged: false, HoldsServiceAccount: false));
        ldap.Setup(x => x.UpdateOrganizationalUnitAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((string dn, string? _, string? _, string? _) => dn);
        ldap.Setup(x => x.UpdateComputerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>()))
            .ReturnsAsync((string dn, string? _, string? _, string? _, string? _, bool? _) => dn);
        ldap.Setup(x => x.DeleteComputerAsync(It.IsAny<string>())).ReturnsAsync(true);

        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.ResolveAsync(CallerDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerHolds);

        // The group in ou=groups is mapped to Admin by its DN, as RoleMappings:GroupDns writes it.
        var mappings = new Mock<IRoleMappingRepository>();
        mappings.Setup(x => x.GetActiveForProviderAsync(It.IsAny<Guid?>()))
            .ReturnsAsync([RoleMapping.Create(null, AdminGroupDn, UserRoles.Admin, 0)]);

        var grants = new Mock<IRolePermissionRepository>();
        grants.Setup(x => x.GetPermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> roles, CancellationToken _) =>
                roles.Contains(UserRoles.Admin) ? Everything : new HashSet<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddScoped<OrganizationalUnitDirectory>();
        services.AddScoped<ComputerDirectory>();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(permissions.Object);
        services.AddSingleton(mappings.Object);
        services.AddSingleton(grants.Object);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("caller@example.com", distinguishedName: CallerDn));
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.Configure<DirectoryProtectionOptions>(options => options.ProtectedGroups = protectedGroups);
        services.WithAuditTrail();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), ldap);
    }

    private static void VerifyUnitUntouched(Mock<ILdapService> ldap) =>
        ldap.Verify(x => x.UpdateOrganizationalUnitAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);

    // --- Organizational units -----------------------------------------------------------------

    [Fact]
    public async Task MovingTheUnitThatHoldsAGroupMappedToAdmin_IsRefused_ToSomeoneWhoIsNotAdmin()
    {
        var (sender, ldap) = Pipeline(Manager);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateOrganizationalUnitCommand(Id(GroupsUnitDn), null, "ou=archive,dc=example,dc=com", null)));
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateOrganizationalUnitCommand(Id(GroupsUnitDn), "groups-old", null, null)));

        VerifyUnitUntouched(ldap);
    }

    [Fact]
    public async Task AnAdministrator_MayMoveIt()
    {
        var (sender, ldap) = Pipeline(Everything);

        await sender.Send(new UpdateOrganizationalUnitCommand(Id(GroupsUnitDn), "groups-old", null, null));

        ldap.Verify(x => x.UpdateOrganizationalUnitAsync(GroupsUnitDn, "groups-old", null, null), Times.Once);
    }

    [Fact]
    public async Task AUnitHoldingNothingOfNote_MovesFreely()
    {
        var (sender, ldap) = Pipeline(Manager);

        await sender.Send(new UpdateOrganizationalUnitCommand(Id(PlainUnitDn), null, "ou=archive,dc=example,dc=com", null));

        ldap.Verify(x => x.UpdateOrganizationalUnitAsync(PlainUnitDn, null, "ou=archive,dc=example,dc=com", null), Times.Once);
    }

    [Fact]
    public async Task AUnitHoldingSomethingPrivilegedInTheDirectory_IsAnAdministratorsToMove()
    {
        var privileged = new DirectorySubtree(HoldsPrivileged: true, HoldsServiceAccount: false);

        var (refused, ldap) = Pipeline(Manager, privileged);
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => refused.Send(new UpdateOrganizationalUnitCommand(Id(PlainUnitDn), "projects-old", null, null)));
        VerifyUnitUntouched(ldap);

        var (allowed, _) = Pipeline(Everything, privileged);
        await allowed.Send(new UpdateOrganizationalUnitCommand(Id(PlainUnitDn), "projects-old", null, null));
    }

    [Fact]
    public async Task AUnitHoldingAProtectedGroup_IsAnAdministratorsToMove()
    {
        // OpenLDAP has no adminCount; DirectoryProtection is how a group is marked privileged there.
        var (sender, ldap) = Pipeline(Manager, beneath: null, "cn=ldap-admins,ou=projects,dc=example,dc=com");

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateOrganizationalUnitCommand(Id(PlainUnitDn), "projects-old", null, null)));

        VerifyUnitUntouched(ldap);
    }

    [Fact]
    public async Task AUnitHoldingTheServiceAccount_IsNobodysToMove()
    {
        var (sender, ldap) = Pipeline(Everything, new DirectorySubtree(HoldsPrivileged: false, HoldsServiceAccount: true));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateOrganizationalUnitCommand(Id(PlainUnitDn), "projects-old", null, null)));

        VerifyUnitUntouched(ldap);
    }

    [Fact]
    public async Task ANewDescriptionAlone_ReaddressesNothing_SoNothingBeneathIsAskedAbout()
    {
        var (sender, ldap) = Pipeline(Manager);

        // The same name, in another case, is not a rename either.
        await sender.Send(new UpdateOrganizationalUnitCommand(Id(GroupsUnitDn), "GROUPS", null, "Every group we map"));

        ldap.Verify(x => x.InspectSubtreeAsync(It.IsAny<string>()), Times.Never);
        ldap.Verify(x => x.UpdateOrganizationalUnitAsync(GroupsUnitDn, "GROUPS", null, "Every group we map"), Times.Once);
    }

    // --- Computers ----------------------------------------------------------------------------

    [Fact]
    public async Task ADomainController_IsAnAdministratorsToChangeOrDelete()
    {
        var (sender, ldap) = Pipeline(Manager);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new UpdateComputerCommand(Id(DomainController.Dn), null, null, null, null, Enabled: false)));
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new DeleteComputerCommand(Id(DomainController.Dn))));

        ldap.Verify(x => x.DeleteComputerAsync(It.IsAny<string>()), Times.Never);
        ldap.Verify(x => x.UpdateComputerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);

        var (allowed, _) = Pipeline(Everything);
        Assert.True(await allowed.Send(new DeleteComputerCommand(Id(DomainController.Dn))));
    }

    [Fact]
    public async Task AWorkstation_IsAComputerManagersToChange()
    {
        var (sender, ldap) = Pipeline(Manager);

        await sender.Send(new UpdateComputerCommand(Id(Workstation.Dn), null, null, null, null, Enabled: false));
        Assert.True(await sender.Send(new DeleteComputerCommand(Id(Workstation.Dn))));

        ldap.Verify(x => x.DeleteComputerAsync(Workstation.Dn), Times.Once);
    }

    [Fact]
    public async Task TheServiceAccount_KeptAsAComputer_IsNobodysToChange()
    {
        var (sender, ldap) = Pipeline(Everything);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new DeleteComputerCommand(Id(ManagedServiceAccount.Dn))));

        ldap.Verify(x => x.DeleteComputerAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("532480", true)]   // SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION: a domain controller
    [InlineData("83890176", true)] // PARTIAL_SECRETS_ACCOUNT | WORKSTATION_TRUST_ACCOUNT: a read-only one
    [InlineData("4096", false)]    // WORKSTATION_TRUST_ACCOUNT
    [InlineData("4098", false)]    // ... disabled, still a workstation
    public void WhatMakesAMachinePrivileged_TheDomainControllerBits(string userAccountControl, bool privileged)
    {
        var attrs = new LdapAttributeSet { new LdapAttribute("userAccountControl", userAccountControl) };

        Assert.Equal(privileged, StandardLdapProvider.IsPrivilegedMachine(attrs));
    }

    [Theory]
    [InlineData("adminCount", "1")]
    [InlineData("isCriticalSystemObject", "TRUE")]
    public void WhatMakesAMachinePrivileged_TheDirectorysOwnMarks(string attribute, string value)
    {
        var attrs = new LdapAttributeSet
        {
            new LdapAttribute("userAccountControl", "4096"),
            new LdapAttribute(attribute, value)
        };

        Assert.True(StandardLdapProvider.IsPrivilegedMachine(attrs));
    }

    [Fact]
    public void TheSubtreeSearch_AsksForEveryMark()
    {
        Assert.Contains("(adminCount>=1)", StandardLdapProvider.PrivilegedEntryFilter);
        Assert.Contains("(isCriticalSystemObject=TRUE)", StandardLdapProvider.PrivilegedEntryFilter);
        Assert.Contains(":1.2.840.113556.1.4.803:=8192)", StandardLdapProvider.PrivilegedEntryFilter);
        Assert.Contains(":1.2.840.113556.1.4.803:=67108864)", StandardLdapProvider.PrivilegedEntryFilter);
    }
}
