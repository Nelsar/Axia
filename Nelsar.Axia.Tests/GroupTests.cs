using Nelsar.Axia.Application.Features.Groups.Queries;
using Nelsar.Axia.Application.Features.Groups.Commands;
using Nelsar.Axia.Application.Features.Groups;
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
/// Group management. The themes are the ones organizational units established — a group's
/// identity is its distinguished name, the API id is only a hash of it, and "we could not read
/// the directory" must never be answered as "there is no such group" — plus one of its own:
/// membership is a mandatory attribute, so emptying a group is a request the directory cannot
/// satisfy and is refused with a reason rather than attempted.
/// </summary>
public class GroupTests
{
    private static LdapGroup Group(
        string dn, string name, string parentDn, string description = "", params string[] members) =>
        new() { Dn = dn, Name = name, ParentDn = parentDn, Description = description, Members = members };

    private static readonly LdapGroup Admins = Group(
        "CN=Администраторы,OU=Groups,DC=example,DC=com", "Администраторы", "OU=Groups,DC=example,DC=com",
        "Полный доступ", "uid=jsmith,DC=example,DC=com");

    private static readonly LdapGroup Sales = Group(
        "CN=Sales,OU=Groups,DC=example,DC=com", "Sales", "OU=Groups,DC=example,DC=com",
        "", "uid=akim,DC=example,DC=com", "uid=bob,DC=example,DC=com");

    private static readonly LdapGroup Auditors = Group(
        "CN=Auditors,DC=example,DC=com", "Auditors", "DC=example,DC=com",
        "Только чтение", "uid=jsmith,DC=example,DC=com");

    /// <summary>The real pipeline over a mocked directory — validators and behaviours
    /// included, so these tests exercise the request exactly as the endpoint issues it.</summary>
    private static ISender CreateSender(Mock<ILdapService> ldap)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();
        services.AddScoped<GroupDirectory>();
        services.AddSingleton(ldap.Object);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static Mock<ILdapService> DirectoryHolding(params LdapGroup[] groups)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetGroupsAsync())
            .ReturnsAsync(DirectoryGroupListing.Complete(groups));
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
    public async Task Search_ReturnsEveryGroup_WithAnIdDerivedFromItsDn()
    {
        var sender = CreateSender(DirectoryHolding(Admins, Sales, Auditors));

        var result = await sender.Send(new SearchGroupsQuery(null, 50, null));

        Assert.Equal(3, result.Items.Count());
        var admins = result.Items.Single(g => g.DistinguishedName == Admins.Dn);
        Assert.Equal(DeterministicIdFactory.BuildDirectoryObjectId(Admins.Dn), admins.Id);
        Assert.Equal("Администраторы", admins.Name);
        Assert.Equal("OU=Groups,DC=example,DC=com", admins.ParentDistinguishedName);
        Assert.Equal("Полный доступ", admins.Description);
        Assert.Equal(["uid=jsmith,DC=example,DC=com"], admins.Members);
    }

    [Fact]
    public async Task Search_IsStableAcrossPages_BecauseTheDirectoryPromisesNoOrder()
    {
        // The same three groups handed back in a different order — as a directory is free to do.
        var first = await CreateSender(DirectoryHolding(Admins, Sales, Auditors)).Send(new SearchGroupsQuery(null, 2, null));
        var second = await CreateSender(DirectoryHolding(Sales, Auditors, Admins)).Send(new SearchGroupsQuery(null, 2, null));

        Assert.Equal(
            first.Items.Select(g => g.DistinguishedName),
            second.Items.Select(g => g.DistinguishedName));
        Assert.True(first.HasMore);
        Assert.Equal("2", first.PageToken);
    }

    [Fact]
    public async Task Search_MatchesNameDnAndDescription_AndRequiresEveryTerm()
    {
        var sender = CreateSender(DirectoryHolding(Admins, Sales, Auditors));

        Assert.Equal("Администраторы", (await sender.Send(new SearchGroupsQuery("доступ", 50, null))).Items.Single().Name);
        Assert.Equal("Sales", (await sender.Send(new SearchGroupsQuery("sales", 50, null))).Items.Single().Name);
        Assert.Equal(2, (await sender.Send(new SearchGroupsQuery("groups", 50, null))).Items.Count());
        Assert.Empty((await sender.Send(new SearchGroupsQuery("sales доступ", 50, null))).Items);
    }

    [Fact]
    public async Task Search_DoesNotMatchMemberDns()
    {
        // "which groups contain jsmith" is a different question, and answering it here would
        // return groups whose own name and description have nothing to do with the term.
        var result = await CreateSender(DirectoryHolding(Admins, Sales, Auditors)).Send(new SearchGroupsQuery("jsmith", 50, null));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Search_ReportsATruncatedEnumerationAsIncomplete()
    {
        // The page that was read is real; what it is not is the whole directory.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetGroupsAsync())
            .ReturnsAsync(DirectoryGroupListing.Truncated(new[] { Admins }));

        var result = await CreateSender(ldap).Send(new SearchGroupsQuery(null, 50, null));

        Assert.False(result.IsComplete);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetById_FindsTheGroupWhoseDnHashesToThatId()
    {
        var sender = CreateSender(DirectoryHolding(Admins, Sales));

        var found = await sender.Send(new GetGroupByIdQuery(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn)));

        Assert.Equal(Sales.Dn, found!.DistinguishedName);
        Assert.Equal(2, found.Members.Count());
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenTheDirectoryWasReadInFullAndHeldNoSuchGroup()
    {
        var sender = CreateSender(DirectoryHolding(Admins));

        Assert.Null(await sender.Send(new GetGroupByIdQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetById_Fails_RatherThanReport404_WhenTheEnumerationWasCutShort()
    {
        // Not finding an id in a listing that stopped early says nothing: the group may sit in a
        // page that was never read. A 404 here would tell an operator their group is gone.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetGroupsAsync())
            .ReturnsAsync(DirectoryGroupListing.Truncated(new[] { Admins }));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => CreateSender(ldap).Send(new GetGroupByIdQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task Create_RejectsABlankName_BeforeTouchingTheDirectory()
    {
        var ldap = DirectoryHolding();
        var sender = CreateSender(ldap);

        await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new CreateGroupCommand("   ", string.Empty, string.Empty, ["uid=jsmith,DC=example,DC=com"])));

        ldap.Verify(
            x => x.CreateGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_RejectsAGroupWithNoMembers_BecauseTheSchemaCannotHoldOne()
    {
        // groupOfNames makes 'member' mandatory. Sending the add anyway would come back as an
        // object-class violation, which tells the operator nothing about what to do differently.
        var ldap = DirectoryHolding();
        var sender = CreateSender(ldap);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new CreateGroupCommand("Sales", string.Empty, string.Empty, Array.Empty<string>())));

        // Blanks are not members either, so a list of them is still an empty group.
        await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new CreateGroupCommand("Sales", string.Empty, string.Empty, ["", "   "])));

        ldap.Verify(
            x => x.CreateGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_DescribesTheEntryTheDirectoryActuallyWrote()
    {
        // The request carried no parent. Where that lands is the adapter's decision (the base
        // DN), so the response is built from the DN it reports back, not from the request.
        var ldap = DirectoryHolding();
        ldap.Setup(x => x.CreateGroupAsync(
                "Аудиторы", string.Empty, "Только чтение",
                It.Is<IReadOnlyList<string>>(m => m.SequenceEqual(new[] { "uid=jsmith,DC=example,DC=com" }))))
            .ReturnsAsync("cn=Аудиторы,DC=example,DC=com");

        var created = await CreateSender(ldap).Send(new CreateGroupCommand("Аудиторы", string.Empty, "Только чтение", ["uid=jsmith,DC=example,DC=com"]));

        Assert.Equal("cn=Аудиторы,DC=example,DC=com", created.DistinguishedName);
        Assert.Equal("Аудиторы", created.Name);
        Assert.Equal("DC=example,DC=com", created.ParentDistinguishedName);
        Assert.Equal(
            DeterministicIdFactory.BuildDirectoryObjectId("cn=Аудиторы,DC=example,DC=com"),
            created.Id);
    }

    [Fact]
    public async Task Create_TrimsAndDeduplicatesMembers()
    {
        // A repeated member is not an extra member — the directory answers a duplicate value with
        // attributeOrValueExists and fails the whole add.
        var ldap = DirectoryHolding();
        ldap.Setup(x => x.CreateGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync("cn=Sales,DC=example,DC=com");

        await CreateSender(ldap).Send(new CreateGroupCommand("Sales", string.Empty, string.Empty, [" uid=akim,DC=example,DC=com ", "UID=AKIM,DC=example,DC=com", "uid=bob,DC=example,DC=com"]));

        ldap.Verify(
            x => x.CreateGroupAsync(
                "Sales", string.Empty, string.Empty,
                It.Is<IReadOnlyList<string>>(m => m.SequenceEqual(
                    new[] { "uid=akim,DC=example,DC=com", "uid=bob,DC=example,DC=com" }))),
            Times.Once);
    }

    [Fact]
    public async Task Update_PassesTheResolvedDn_AndReturnsWhereTheEntryEndedUp()
    {
        var ldap = DirectoryHolding(Sales);
        ldap.Setup(x => x.UpdateGroupAsync(Sales.Dn, "Продажи", null, null, null))
            .ReturnsAsync("cn=Продажи,OU=Groups,DC=example,DC=com");

        var updated = await CreateSender(ldap).Send(new UpdateGroupCommand(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn), "Продажи", null, null, null));

        Assert.Equal("cn=Продажи,OU=Groups,DC=example,DC=com", updated!.DistinguishedName);
        // Renaming re-addresses the entry, so its id moves with its DN rather than outliving it.
        Assert.NotEqual(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn), updated.Id);
    }

    [Fact]
    public async Task Update_LeavesDescriptionAndMembershipAlone_WhenTheRequestDoesNotMentionThem()
    {
        var ldap = DirectoryHolding(Admins);
        ldap.Setup(x => x.UpdateGroupAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>()))
            .ReturnsAsync(Admins.Dn);

        var updated = await CreateSender(ldap).Send(new UpdateGroupCommand(DeterministicIdFactory.BuildDirectoryObjectId(Admins.Dn), "Администраторы", null, null, null));

        // Non-nullable DTO defaults would have sent "" and an empty member list here, wiping a
        // description and emptying a group the caller never mentioned.
        ldap.Verify(x => x.UpdateGroupAsync(Admins.Dn, "Администраторы", null, null, null), Times.Once);
        Assert.Equal("Полный доступ", updated!.Description);
        Assert.Equal(["uid=jsmith,DC=example,DC=com"], updated.Members);
    }

    [Fact]
    public async Task Update_ReplacesMembershipWholesale_WhenAListIsGiven()
    {
        var ldap = DirectoryHolding(Sales);
        ldap.Setup(x => x.UpdateGroupAsync(
                Sales.Dn, null, null, null,
                It.Is<IReadOnlyList<string>?>(m => m != null && m.SequenceEqual(new[] { "uid=cara,DC=example,DC=com" }))))
            .ReturnsAsync(Sales.Dn);

        var updated = await CreateSender(ldap).Send(new UpdateGroupCommand(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn), null, null, null, ["uid=cara,DC=example,DC=com"]));

        Assert.Equal(["uid=cara,DC=example,DC=com"], updated!.Members);
    }

    [Fact]
    public async Task Update_RefusesToEmptyTheMembership_RatherThanLetTheDirectoryRejectIt()
    {
        var ldap = DirectoryHolding(Sales);
        var sender = CreateSender(ldap);

        await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new UpdateGroupCommand(DeterministicIdFactory.BuildDirectoryObjectId(Sales.Dn), null, null, null, [])));

        ldap.Verify(
            x => x.UpdateGroupAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateAndDelete_AnswerNotFound_ForAnIdNoGroupCarries()
    {
        var sender = CreateSender(DirectoryHolding(Admins));

        Assert.Null(await sender.Send(new UpdateGroupCommand(Guid.NewGuid(), null, null, null, null)));
        Assert.False(await sender.Send(new DeleteGroupCommand(Guid.NewGuid())));
    }

    [Fact]
    public async Task Delete_AddressesTheDirectoryByDn()
    {
        var ldap = DirectoryHolding(Auditors);
        ldap.Setup(x => x.DeleteGroupAsync(Auditors.Dn)).ReturnsAsync(true);

        var deleted = await CreateSender(ldap).Send(new DeleteGroupCommand(DeterministicIdFactory.BuildDirectoryObjectId(Auditors.Dn)));

        Assert.True(deleted);
        ldap.Verify(x => x.DeleteGroupAsync(Auditors.Dn), Times.Once);
    }

    [Fact]
    public async Task TheEndpointIsLive_NowThatTheServiceIsImplemented()
    {
        // NotImplementedEndpointsTests used to assert 501 for every group route, on the strength
        // of the controller holding no opinion of its own. Implementing the service was indeed
        // all it took — the only change in the API layer was the missing filter parameter below.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetGroupsAsync())
            .ReturnsAsync(DirectoryGroupListing.Complete(new[] { Admins, Sales }));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var response = await AuthorisedClient(factory).GetAsync("/api/groups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponse<GroupDto>>();
        Assert.Equal(2, payload!.Items.Count());
    }

    [Fact]
    public async Task TheEndpointAppliesTheFilter_RatherThanIgnoringIt()
    {
        // The same defect organizational units had: the action declared no filter parameter and
        // passed null, so ?filter= was accepted, dropped, and answered with the whole directory.
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetGroupsAsync())
            .ReturnsAsync(DirectoryGroupListing.Complete(new[] { Admins, Sales, Auditors }));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var response = await AuthorisedClient(factory).GetAsync("/api/groups?filter=auditors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponse<GroupDto>>();
        Assert.Equal("Auditors", payload!.Items.Single().Name);
    }

    [Fact]
    public async Task AProviderThatCannotManageGroups_Answers501_NotAnEmptyList()
    {
        // ActiveDirectoryProvider has no group path of its own and says so by throwing
        // FeatureNotImplementedException, exactly as it does for organizational units. This pins
        // the end of that route: the caller is told the capability is missing, rather than handed
        // an empty page that reads as "this directory has no groups".
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetGroupsAsync())
            .ThrowsAsync(new FeatureNotImplementedException("Group management is not implemented for this provider."));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var response = await AuthorisedClient(factory).GetAsync("/api/groups");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("not implemented", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }
}
