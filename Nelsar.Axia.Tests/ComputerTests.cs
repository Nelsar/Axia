using Nelsar.Axia.Application.Features.Computers.Queries;
using Nelsar.Axia.Application.Features.Computers.Commands;
using Nelsar.Axia.Application.Features.Computers;
using Nelsar.Axia.Application;
using MediatR;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Xunit;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Computer account management. Carries over the themes groups and organizational units settled
/// — the DN is the identity, the API id is only a hash of it, and a truncated enumeration is
/// never reported as absence — and adds one of its own: a computer is an Active Directory
/// concept, so a directory that only knows RFC 4519 <c>device</c> entries reports no operating
/// system and no enabled state, and those must arrive as unknown rather than as defaults.
/// </summary>
public class ComputerTests
{
    private static LdapComputer Computer(
        string dn, string name, string parentDn, string description = "",
        string dnsHostName = "", string os = "", string osVersion = "", bool? enabled = true) =>
        new()
        {
            Dn = dn, Name = name, ParentDn = parentDn, Description = description,
            DnsHostName = dnsHostName, OperatingSystem = os,
            OperatingSystemVersion = osVersion, Enabled = enabled
        };

    private static readonly LdapComputer Ws01 = Computer(
        "CN=WS01,OU=Computers,DC=example,DC=com", "WS01", "OU=Computers,DC=example,DC=com",
        "Бухгалтерия", "ws01.example.com", "Windows 10 Pro", "10.0 (19045)");

    private static readonly LdapComputer Srv2012 = Computer(
        "CN=SRV-FILE,OU=Servers,DC=example,DC=com", "SRV-FILE", "OU=Servers,DC=example,DC=com",
        "Файловый сервер", "srv-file.example.com", "Windows Server 2012 R2", "6.3 (9600)");

    private static readonly LdapComputer Retired = Computer(
        "CN=OLD-PC,DC=example,DC=com", "OLD-PC", "DC=example,DC=com",
        "Списан", enabled: false);

    /// <summary>An RFC 4519 device: no account, so no operating system and no enabled state.</summary>
    private static readonly LdapComputer Printer = Computer(
        "CN=PRN-01,OU=Devices,DC=example,DC=com", "PRN-01", "OU=Devices,DC=example,DC=com",
        "Принтер", enabled: null);

    /// <summary>The real pipeline over a mocked directory — validators and behaviours
    /// included, so these tests exercise the request exactly as the endpoint issues it.</summary>
    private static ISender CreateSender(Mock<ILdapService> ldap)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();
        services.AddScoped<ComputerDirectory>();
        services.AddSingleton(ldap.Object);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static Mock<ILdapService> DirectoryHolding(params LdapComputer[] computers)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetComputersAsync())
            .ReturnsAsync(DirectoryComputerListing.Complete(computers));
        return ldap;
    }

    private static HttpClient AuthorisedClient(ApiTestFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        return client;
    }

    [Fact]
    public async Task Search_ReturnsEveryComputer_WithAnIdDerivedFromItsDn()
    {
        var sender = CreateSender(DirectoryHolding(Ws01, Srv2012, Retired));

        var result = await sender.Send(new SearchComputersQuery(null, 50, null));

        Assert.Equal(3, result.Items.Count());
        var ws01 = result.Items.Single(c => c.DistinguishedName == Ws01.Dn);
        Assert.Equal(DeterministicIdFactory.BuildDirectoryObjectId(Ws01.Dn), ws01.Id);
        Assert.Equal("WS01", ws01.Name);
        Assert.Equal("OU=Computers,DC=example,DC=com", ws01.ParentDistinguishedName);
        Assert.Equal("ws01.example.com", ws01.DnsHostName);
        Assert.Equal("Windows 10 Pro", ws01.OperatingSystem);
        Assert.True(ws01.Enabled);
    }

    [Fact]
    public async Task ADeviceWithNoAccount_ReportsUnknownEnabledState_NotEnabled()
    {
        var sender = CreateSender(DirectoryHolding(Printer));

        var printer = (await sender.Send(new SearchComputersQuery(null, 50, null))).Items.Single();

        // Null, not false and not true. An entry whose state we cannot read must not be shown
        // as a healthy enabled machine, nor as a disabled one nobody needs to look at.
        Assert.Null(printer.Enabled);
        Assert.Equal(string.Empty, printer.OperatingSystem);
    }

    [Fact]
    public async Task ADisabledAccount_IsReportedAsDisabled()
    {
        var sender = CreateSender(DirectoryHolding(Retired));

        Assert.False((await sender.Send(new SearchComputersQuery(null, 50, null))).Items.Single().Enabled);
    }

    [Fact]
    public async Task Search_IsStableAcrossPages_BecauseTheDirectoryPromisesNoOrder()
    {
        var first = await CreateSender(DirectoryHolding(Ws01, Srv2012, Retired)).Send(new SearchComputersQuery(null, 2, null));
        var second = await CreateSender(DirectoryHolding(Retired, Ws01, Srv2012)).Send(new SearchComputersQuery(null, 2, null));

        Assert.Equal(
            first.Items.Select(c => c.DistinguishedName),
            second.Items.Select(c => c.DistinguishedName));
        Assert.True(first.HasMore);
    }

    [Fact]
    public async Task Filter_MatchesOperatingSystem_BecauseThatIsHowAnInventoryIsSearched()
    {
        var sender = CreateSender(DirectoryHolding(Ws01, Srv2012, Retired));

        var result = await sender.Send(new SearchComputersQuery("2012", 50, null));

        Assert.Equal("SRV-FILE", result.Items.Single().Name);
    }

    [Fact]
    public async Task Filter_MatchesDnsHostName()
    {
        var sender = CreateSender(DirectoryHolding(Ws01, Srv2012));

        var result = await sender.Send(new SearchComputersQuery("srv-file.example.com", 50, null));

        Assert.Equal("SRV-FILE", result.Items.Single().Name);
    }

    [Fact]
    public async Task AnIdMissedByATruncatedEnumeration_Raises_RatherThanReporting404()
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetComputersAsync())
            .ReturnsAsync(DirectoryComputerListing.Truncated([Ws01]));

        var sender = CreateSender(ldap);

        // Not finding an id in a walk that stopped early says nothing about whether the account
        // exists. Answering 404 here would deny a machine on the strength of a failed search.
        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => sender.Send(new GetComputerByIdQuery(DeterministicIdFactory.BuildDirectoryObjectId(Srv2012.Dn))));
    }

    [Fact]
    public async Task AnIdMissedByACompleteEnumeration_IsGenuinelyAbsent()
    {
        var sender = CreateSender(DirectoryHolding(Ws01));

        Assert.Null(await sender.Send(new GetComputerByIdQuery(DeterministicIdFactory.BuildDirectoryObjectId(Srv2012.Dn))));
    }

    [Fact]
    public async Task Create_RejectsABlankName_BeforeTouchingTheDirectory()
    {
        var ldap = DirectoryHolding();
        var sender = CreateSender(ldap);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new CreateComputerCommand("   ", string.Empty, string.Empty, string.Empty)));

        ldap.Verify(
            x => x.CreateComputerAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_BuildsTheResultFromTheDnTheDirectoryActuallyWrote()
    {
        var ldap = DirectoryHolding();
        ldap.Setup(x => x.CreateComputerAsync("WS99", string.Empty, "Новый", "ws99.example.com"))
            .ReturnsAsync("CN=WS99,OU=Computers,DC=example,DC=com");

        var created = await CreateSender(ldap).Send(new CreateComputerCommand("WS99", string.Empty, "Новый", "ws99.example.com"));

        // A blank parent resolves to the base DN, and only the adapter knows what that is — so
        // the id and the parent both have to come from the DN it returned.
        Assert.Equal("CN=WS99,OU=Computers,DC=example,DC=com", created.DistinguishedName);
        Assert.Equal("OU=Computers,DC=example,DC=com", created.ParentDistinguishedName);
        Assert.Equal(
            DeterministicIdFactory.BuildDirectoryObjectId("CN=WS99,OU=Computers,DC=example,DC=com"),
            created.Id);
        Assert.True(created.Enabled);
        // Not invented: a pre-staged account has no operating system until a machine joins.
        Assert.Equal(string.Empty, created.OperatingSystem);
    }

    [Fact]
    public async Task Update_LeavesOmittedFieldsAlone()
    {
        var ldap = DirectoryHolding(Ws01);
        ldap.Setup(x => x.UpdateComputerAsync(Ws01.Dn, "WS01-NEW", null, null, null, null))
            .ReturnsAsync("CN=WS01-NEW,OU=Computers,DC=example,DC=com");

        var updated = await CreateSender(ldap).Send(new UpdateComputerCommand(DeterministicIdFactory.BuildDirectoryObjectId(Ws01.Dn), "WS01-NEW", null, null, null, null));

        // Renaming must not wipe the description or the DNS name. Null means "leave it", and a
        // non-nullable DTO default of "" is exactly how that gets broken.
        Assert.Equal("Бухгалтерия", updated!.Description);
        Assert.Equal("ws01.example.com", updated.DnsHostName);
        Assert.Equal("Windows 10 Pro", updated.OperatingSystem);
        Assert.True(updated.Enabled);
    }

    [Fact]
    public async Task Update_ChangesTheId_BecauseARenameChangesTheDn()
    {
        var ldap = DirectoryHolding(Ws01);
        ldap.Setup(x => x.UpdateComputerAsync(Ws01.Dn, "WS01-NEW", null, null, null, null))
            .ReturnsAsync("CN=WS01-NEW,OU=Computers,DC=example,DC=com");

        var id = DeterministicIdFactory.BuildDirectoryObjectId(Ws01.Dn);
        var updated = await CreateSender(ldap).Send(new UpdateComputerCommand(id, "WS01-NEW", null, null, null, null));

        Assert.NotEqual(id, updated!.Id);
        Assert.Equal(
            DeterministicIdFactory.BuildDirectoryObjectId("CN=WS01-NEW,OU=Computers,DC=example,DC=com"),
            updated.Id);
    }

    [Fact]
    public async Task Update_CanDisableAnAccount()
    {
        var ldap = DirectoryHolding(Ws01);
        ldap.Setup(x => x.UpdateComputerAsync(Ws01.Dn, null, null, null, null, false))
            .ReturnsAsync(Ws01.Dn);

        var updated = await CreateSender(ldap).Send(new UpdateComputerCommand(DeterministicIdFactory.BuildDirectoryObjectId(Ws01.Dn), null, null, null, null, false));

        Assert.False(updated!.Enabled);
    }

    [Fact]
    public async Task Delete_IsFalse_WhenThereIsNoSuchAccount()
    {
        var sender = CreateSender(DirectoryHolding(Ws01));

        Assert.False(await sender.Send(new DeleteComputerCommand(DeterministicIdFactory.BuildDirectoryObjectId(Srv2012.Dn))));
    }

    [Fact]
    public async Task Delete_AddressesTheDirectoryByDn_NotById()
    {
        var ldap = DirectoryHolding(Ws01);
        ldap.Setup(x => x.DeleteComputerAsync(It.IsAny<string>())).ReturnsAsync(true);

        await CreateSender(ldap).Send(new DeleteComputerCommand(DeterministicIdFactory.BuildDirectoryObjectId(Ws01.Dn)));

        ldap.Verify(x => x.DeleteComputerAsync(Ws01.Dn), Times.Once);
    }

    [Fact]
    public async Task TheListingEndpointAppliesItsFilter()
    {
        // The defect groups and units both shipped with: the action declared no filter parameter
        // and passed null, so ?filter= was accepted, dropped, and answered with everything.
        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(DirectoryHolding(Ws01, Srv2012).Object);
        });

        var response = await AuthorisedClient(factory).GetAsync("/api/v1/computers?filter=SRV-FILE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponse<ComputerDto>>();
        Assert.Equal("SRV-FILE", payload!.Items.Single().Name);
    }

    [Fact]
    public async Task AProviderThatCannotManageComputers_Answers501_NotAnEmptyList()
    {
        // ActiveDirectoryProvider has no computer path of its own and says so by throwing,
        // exactly as it does for groups and organizational units. An empty page would read as
        // "this directory has no computers", which is a different and false statement.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetComputersAsync())
            .ThrowsAsync(new FeatureNotImplementedException("Computer management is not implemented for this provider."));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var response = await AuthorisedClient(factory).GetAsync("/api/v1/computers");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("not implemented", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }
}
