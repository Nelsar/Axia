using Nelsar.Axia.Application.Features.OrganizationalUnits.Queries;
using Nelsar.Axia.Application.Features.OrganizationalUnits.Commands;
using Nelsar.Axia.Application.Features.OrganizationalUnits;
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
/// Organizational unit management. The theme running through these: a unit's identity is its
/// distinguished name, the API id is only a hash of it, and "we could not read the directory"
/// must never be answered as "there is no such unit".
/// </summary>
public class OrganizationalUnitTests
{
    private static LdapOrganizationalUnit Unit(string dn, string name, string parentDn, string description = "") =>
        new() { Dn = dn, Name = name, ParentDn = parentDn, Description = description };

    private static readonly LdapOrganizationalUnit Finance =
        Unit("OU=Финансы,DC=example,DC=com", "Финансы", "DC=example,DC=com", "Бухгалтерия");

    private static readonly LdapOrganizationalUnit Sales =
        Unit("OU=Sales,DC=example,DC=com", "Sales", "DC=example,DC=com");

    private static readonly LdapOrganizationalUnit Branch =
        Unit("OU=Almaty,OU=Sales,DC=example,DC=com", "Almaty", "OU=Sales,DC=example,DC=com");

    /// <summary>The real pipeline over a mocked directory — validators and behaviours
    /// included, so these tests exercise the request exactly as the endpoint issues it.</summary>
    private static ISender CreateSender(Mock<ILdapService> ldap)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();
        services.AddScoped<OrganizationalUnitDirectory>();
        services.AddSingleton(ldap.Object);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static Mock<ILdapService> DirectoryHolding(params LdapOrganizationalUnit[] units)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetOrganizationalUnitsAsync())
            .ReturnsAsync(DirectoryOrganizationalUnitListing.Complete(units));
        return ldap;
    }

    [Fact]
    public async Task Search_ReturnsEveryUnit_WithAnIdDerivedFromItsDn()
    {
        var sender = CreateSender(DirectoryHolding(Finance, Sales, Branch));

        var result = await sender.Send(new SearchOrganizationalUnitsQuery(null, 50, null));

        Assert.Equal(3, result.Items.Count());
        var finance = result.Items.Single(u => u.DistinguishedName == Finance.Dn);
        Assert.Equal(DeterministicIdFactory.BuildDirectoryObjectId(Finance.Dn), finance.Id);
        Assert.Equal("Финансы", finance.Name);
        Assert.Equal("DC=example,DC=com", finance.ParentDistinguishedName);
        Assert.Equal("Бухгалтерия", finance.Description);
    }

    [Fact]
    public async Task Search_IsStableAcrossPages_BecauseTheDirectoryPromisesNoOrder()
    {
        // Same three units, handed back in a different order — as a directory is free to do.
        var first = await CreateSender(DirectoryHolding(Finance, Sales, Branch))
            .Send(new SearchOrganizationalUnitsQuery(null, 2, null));
        var second = await CreateSender(DirectoryHolding(Sales, Branch, Finance))
            .Send(new SearchOrganizationalUnitsQuery(null, 2, null));

        Assert.Equal(
            first.Items.Select(u => u.DistinguishedName),
            second.Items.Select(u => u.DistinguishedName));
        Assert.True(first.HasMore);
        Assert.Equal("2", first.PageToken);
    }

    [Fact]
    public async Task Search_MatchesNameDnAndDescription_AndRequiresEveryTerm()
    {
        var sender = CreateSender(DirectoryHolding(Finance, Sales, Branch));

        Assert.Equal("Финансы", (await sender.Send(new SearchOrganizationalUnitsQuery("бухгалт", 50, null))).Items.Single().Name);
        Assert.Equal("Almaty", (await sender.Send(new SearchOrganizationalUnitsQuery("almaty", 50, null))).Items.Single().Name);
        Assert.Equal(2, (await sender.Send(new SearchOrganizationalUnitsQuery("sales", 50, null))).Items.Count());
        Assert.Empty((await sender.Send(new SearchOrganizationalUnitsQuery("sales финансы", 50, null))).Items);
    }

    [Fact]
    public async Task Search_ReportsATruncatedEnumerationAsIncomplete()
    {
        // The page that was read is real; what it is not is the whole directory. A client that
        // cannot tell the difference will present a partial list as the full picture.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetOrganizationalUnitsAsync())
            .ReturnsAsync(DirectoryOrganizationalUnitListing.Truncated(new[] { Finance }));

        var result = await CreateSender(ldap).Send(new SearchOrganizationalUnitsQuery(null, 50, null));

        Assert.False(result.IsComplete);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetById_FindsTheUnitWhoseDnHashesToThatId()
    {
        var sender = CreateSender(DirectoryHolding(Finance, Sales));

        var found = await sender.Send(new GetOrganizationalUnitByIdQuery(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn)));

        Assert.Equal(Sales.Dn, found!.DistinguishedName);
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenTheDirectoryWasReadInFullAndHeldNoSuchUnit()
    {
        var sender = CreateSender(DirectoryHolding(Finance));

        Assert.Null(await sender.Send(new GetOrganizationalUnitByIdQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetById_Fails_RatherThanReport404_WhenTheEnumerationWasCutShort()
    {
        // Not finding an id in a listing that stopped early says nothing: the unit may sit in a
        // page that was never read. A 404 here would tell an operator their OU is gone.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetOrganizationalUnitsAsync())
            .ReturnsAsync(DirectoryOrganizationalUnitListing.Truncated(new[] { Finance }));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => CreateSender(ldap).Send(new GetOrganizationalUnitByIdQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task Create_RejectsABlankName_BeforeTouchingTheDirectory()
    {
        var ldap = DirectoryHolding();
        var sender = CreateSender(ldap);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new CreateOrganizationalUnitCommand("   ", string.Empty, string.Empty)));

        ldap.Verify(
            x => x.CreateOrganizationalUnitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_DescribesTheEntryTheDirectoryActuallyWrote()
    {
        // The request carried no parent. Where that lands is the adapter's decision (the base
        // DN), so the response is built from the DN it reports back, not from the request.
        var ldap = DirectoryHolding();
        ldap.Setup(x => x.CreateOrganizationalUnitAsync("Финансы", string.Empty, "Бухгалтерия"))
            .ReturnsAsync("ou=Финансы,DC=example,DC=com");

        var created = await CreateSender(ldap).Send(new CreateOrganizationalUnitCommand("Финансы", string.Empty, "Бухгалтерия"));

        Assert.Equal("ou=Финансы,DC=example,DC=com", created.DistinguishedName);
        Assert.Equal("Финансы", created.Name);
        Assert.Equal("DC=example,DC=com", created.ParentDistinguishedName);
        Assert.Equal(
            DeterministicIdFactory.BuildDirectoryObjectId("ou=Финансы,DC=example,DC=com"),
            created.Id);
    }

    [Fact]
    public async Task Update_PassesTheResolvedDn_AndReturnsWhereTheEntryEndedUp()
    {
        var ldap = DirectoryHolding(Sales);
        ldap.Setup(x => x.UpdateOrganizationalUnitAsync(Sales.Dn, "Продажи", null, null))
            .ReturnsAsync("ou=Продажи,DC=example,DC=com");

        var updated = await CreateSender(ldap).Send(new UpdateOrganizationalUnitCommand(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn), "Продажи", null, null));

        Assert.Equal("ou=Продажи,DC=example,DC=com", updated!.DistinguishedName);
        // Renaming re-addresses the entry, so its id moves with its DN rather than outliving it.
        Assert.NotEqual(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn), updated.Id);
    }

    [Fact]
    public async Task Update_LeavesTheDescriptionAlone_WhenTheRequestDoesNotMentionIt()
    {
        var ldap = DirectoryHolding(Finance);
        ldap.Setup(x => x.UpdateOrganizationalUnitAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(Finance.Dn);

        var updated = await CreateSender(ldap).Send(new UpdateOrganizationalUnitCommand(DeterministicIdFactory.BuildDirectoryObjectId(Finance.Dn), "Финансы", null, null));

        // A non-nullable DTO default would have sent "" here and wiped a description the caller
        // never mentioned.
        ldap.Verify(x => x.UpdateOrganizationalUnitAsync(Finance.Dn, "Финансы", null, null), Times.Once);
        Assert.Equal("Бухгалтерия", updated!.Description);
    }

    [Fact]
    public async Task UpdateAndDelete_AnswerNotFound_ForAnIdNoUnitCarries()
    {
        var sender = CreateSender(DirectoryHolding(Finance));

        Assert.Null(await sender.Send(new UpdateOrganizationalUnitCommand(Guid.NewGuid(), null, null, null)));
        Assert.False(await sender.Send(new DeleteOrganizationalUnitCommand(Guid.NewGuid())));
    }

    [Fact]
    public async Task Delete_AddressesTheDirectoryByDn()
    {
        var ldap = DirectoryHolding(Branch);
        ldap.Setup(x => x.DeleteOrganizationalUnitAsync(Branch.Dn)).ReturnsAsync(true);

        var deleted = await CreateSender(ldap).Send(new DeleteOrganizationalUnitCommand(DeterministicIdFactory.BuildDirectoryObjectId(Branch.Dn)));

        Assert.True(deleted);
        ldap.Verify(x => x.DeleteOrganizationalUnitAsync(Branch.Dn), Times.Once);
    }

    [Fact]
    public async Task TheEndpointIsLive_NowThatTheServiceIsImplemented()
    {
        // NotImplementedEndpointsTests used to assert 501 for this route, on the strength of the
        // controller holding no opinion of its own. Implementing the service was indeed all it
        // took — nothing in the API layer changed.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetOrganizationalUnitsAsync())
            .ReturnsAsync(DirectoryOrganizationalUnitListing.Complete(new[] { Finance, Sales }));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.GetAsync("/api/v1/organizational-units");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponse<OrganizationalUnitDto>>();
        Assert.Equal(2, payload!.Items.Count());
    }

    [Fact]
    public async Task TheEndpointAppliesTheFilter_RatherThanIgnoringIt()
    {
        // Found by running against a real directory: the action took no filter parameter and
        // passed null to the service, so ?filter= was accepted, dropped, and answered with the
        // entire directory — a search that silently matches everything.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetOrganizationalUnitsAsync())
            .ReturnsAsync(DirectoryOrganizationalUnitListing.Complete(new[] { Finance, Sales, Branch }));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.GetAsync("/api/v1/organizational-units?filter=almaty");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponse<OrganizationalUnitDto>>();
        Assert.Equal("Almaty", payload!.Items.Single().Name);
    }
}
