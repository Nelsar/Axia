using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Users;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Events;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Xunit;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Tests;

public class UserServiceTests
{
    /// <summary>
    /// Builds the real Application pipeline over mocked collaborators.
    ///
    /// Dispatching through <see cref="ISender"/> rather than newing up a handler is deliberate:
    /// it puts the registered validators and pipeline behaviours in the path, so these tests
    /// cover the request exactly as an endpoint issues it. Each call gets its own provider, and
    /// therefore its own memory cache — the same isolation the per-test <c>new MemoryCache</c>
    /// used to give.
    /// </summary>
    private static ISender CreateSender(
        ILdapService ldapService,
        IBulkJobQueue? bulkJobQueue = null,
        IBulkJobStore? bulkJobStore = null,
        IDomainEventPublisher? events = null,
        IUserLookupRepository? userLookup = null,
        ITokenService? tokenService = null)
    {
        var tokenServiceMock = new Mock<ITokenService>();
        tokenServiceMock.Setup(x => x.GenerateToken(It.IsAny<UserDto>())).Returns("test-token");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();

        services.AddScoped<UserDirectory>();
        services.AddSingleton(ldapService);
        services.AddSingleton(tokenService ?? tokenServiceMock.Object);
        services.AddSingleton(bulkJobQueue ?? new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(bulkJobStore ?? new Mock<IBulkJobStore>().Object);
        services.AddSingleton(events ?? new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(userLookup ?? new Mock<IUserLookupRepository>().Object);

        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenLdapAuthenticationFails()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync("user@example.com", "pass"))
            .ReturnsAsync((string?)null);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new AuthenticateCommand("user@example.com", "pass"));

        Assert.Null(response);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsToken_WhenLdapAuthenticationSucceeds()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("uid=user,ou=people,dc=example,dc=com");
        ldapServiceMock.Setup(x => x.GetUserByDnAsync("uid=user,ou=people,dc=example,dc=com"))
            .ReturnsAsync(new LdapUserInfo
            {
                Email = "user@example.com",
                DisplayName = "User Example",
                Dn = "uid=user,ou=people,dc=example,dc=com"
            });
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<LdapUserInfo>()))
            .ReturnsAsync(UserRoles.Admin);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new AuthenticateCommand("user@example.com", "pass"));

        Assert.NotNull(response);
        Assert.NotEmpty(response.Token);
        Assert.Equal("user@example.com", response.User.Email);
        Assert.Equal(UserRoles.Admin, response.User.Role);
    }

    [Fact]
    public async Task CreateAsync_ThrowsDomainConflict_WhenUserAlreadyExists()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("user@example.com"))
            .ReturnsAsync(new LdapUserInfo());

        var sender = CreateSender(ldapServiceMock.Object);

        await Assert.ThrowsAsync<DomainConflictException>(() => sender.Send(new CreateUserCommand("user@example.com", "User", "pass")));
    }

    [Fact]
    public async Task CreateAsync_ReturnsUserDto_WhenUserDoesNotExist()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("user@example.com"))
            .ReturnsAsync((LdapUserInfo?)null);
        ldapServiceMock.Setup(x => x.CreateUserAsync("User", "user@example.com", "pass"))
            .Returns(Task.CompletedTask);

        var sender = CreateSender(ldapServiceMock.Object);

        var result = await sender.Send(new CreateUserCommand("user@example.com", "User", "pass"));

        Assert.Equal("user@example.com", result.Email);
        Assert.Equal("User", result.Name);
        Assert.Equal(UserRoles.User, result.Role);
    }

    [Fact]
    public async Task ProcessBulkUsersAsync_CreatesAndUpdatesUsers()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("create@example.com"))
            .ReturnsAsync((LdapUserInfo?)null);
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("update@example.com"))
            .ReturnsAsync(new LdapUserInfo
            {
                Dn = "uid=update,ou=people,dc=example,dc=com"
            });
        ldapServiceMock.Setup(x => x.CreateUserAsync("Create User", "create@example.com", "Password1"))
            .Returns(Task.CompletedTask);
        ldapServiceMock.Setup(x => x.UpdateUserAsync("uid=update,ou=people,dc=example,dc=com", "Updated Name", null, "NewPass1"))
            .Returns(Task.CompletedTask);

        var queueMock = new Mock<IBulkJobQueue>();
        var storeMock = new Mock<IBulkJobStore>();

        var sender = CreateSender(ldapServiceMock.Object, queueMock.Object, storeMock.Object);

        var response = await sender.Send(new ProcessBulkUsersCommand(new[]
        {
            new BulkUserRequest
            {
                Operation = "create",
                Email = "create@example.com",
                Name = "Create User",
                Password = "Password1"
            },
            new BulkUserRequest
            {
                Operation = "update",
                Email = "update@example.com",
                Name = "Updated Name",
                Password = "NewPass1"
            }
        }));

        Assert.Equal(2, response.Items.Count());
        Assert.Equal(2, response.CompletedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.All(response.Items, item => Assert.Equal("completed", item.Status));
    }

    [Fact]
    public async Task GenerateUserPatternsAsync_ExpandsNumericRange()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new GenerateUserPatternsQuery("node-{01..03}", 10, null, null, null, false, false));

        Assert.Equal(3, response.Count);
        Assert.Equal(new[] { "node-01", "node-02", "node-03" }, response.Items);
    }

    [Fact]
    public async Task GenerateUserPatternsAsync_ExtractsNamedGroupsFromRawTextAndAppliesTemplate()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new GenerateUserPatternsQuery(string.Empty, 10, "Ivanov Ivan\nPetrov Petr", @"^(?<lastname>\S+)\s+(?<firstname>\S+)$", "{firstname}.{lastname}", false, false));

        Assert.Equal(2, response.Count);
        Assert.Equal(new[] { "ivan.ivanov", "petr.petrov" }, response.Items);
    }

    [Fact]
    public async Task GenerateUserPatternsAsync_TransliteratesCyrillicNames()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new GenerateUserPatternsQuery(string.Empty, 10, "Иванов Иван", @"^(?<lastname>\S+)\s+(?<firstname>\S+)$", "{firstname}.{lastname}", true, false));

        Assert.Equal(1, response.Count);
        Assert.Equal("ivan.ivanov", response.Items.Single());
    }

    [Fact]
    public async Task GenerateUserPatternsAsync_ReportsConflicts_WhenCheckConflictsIsSet()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("node-01"))
            .ReturnsAsync(new LdapUserInfo { Dn = "uid=node-01,ou=hosts,dc=example,dc=com" });
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("node-02"))
            .ReturnsAsync((LdapUserInfo?)null);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new GenerateUserPatternsQuery("node-{01..02}", 10, null, null, null, false, true));

        Assert.NotNull(response.Details);
        var details = response.Details!.ToList();
        Assert.Equal(2, details.Count);
        Assert.True(details.Single(d => d.Value == "node-01").Exists);
        Assert.False(details.Single(d => d.Value == "node-02").Exists);
    }

    [Fact]
    public async Task AuthenticateAsync_PublishesUserAuthenticationSucceeded_WithActorAndDn()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("uid=user,ou=people,dc=example,dc=com");
        ldapServiceMock.Setup(x => x.GetUserByDnAsync("uid=user,ou=people,dc=example,dc=com"))
            .ReturnsAsync(new LdapUserInfo
            {
                Email = "user@example.com",
                DisplayName = "User Example",
                Dn = "uid=user,ou=people,dc=example,dc=com"
            });
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<LdapUserInfo>()))
            .ReturnsAsync(UserRoles.Admin);

        var events = new Mock<IDomainEventPublisher>();
        var sender = CreateSender(ldapServiceMock.Object, events: events.Object);

        await sender.Send(new AuthenticateCommand("user@example.com", "pass"));

        events.Verify(
            x => x.PublishAsync(
                It.Is<UserAuthenticationSucceeded>(e =>
                    e.Actor == "user@example.com" &&
                    e.Dn == "uid=user,ou=people,dc=example,dc=com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_DescribesTheEntryThePasswordWasProvenAgainst()
    {
        // Two entries answer to the login. The bind landed on the editor's, so the session must be
        // the editor's — not whichever entry a second search by the same login turns up first.
        const string editorDn = "uid=editor,ou=people,dc=example,dc=com";

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync("shared@example.com", "pass")).ReturnsAsync(editorDn);
        ldapServiceMock.Setup(x => x.GetUserByDnAsync(editorDn))
            .ReturnsAsync(new LdapUserInfo { Dn = editorDn, Username = "editor", Email = "shared@example.com" });
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("shared@example.com"))
            .ReturnsAsync(new LdapUserInfo { Dn = "uid=admin,ou=people,dc=example,dc=com", Username = "admin" });
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<LdapUserInfo>())).ReturnsAsync(UserRoles.User);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new AuthenticateCommand("shared@example.com", "pass"));

        Assert.NotNull(response);
        Assert.Equal(editorDn, response.User.Dn);
        Assert.Equal("editor", response.User.Username);
        ldapServiceMock.Verify(x => x.GetUserDetailsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_PublishesUserAuthenticationFailed_WhenCredentialsAreRejected()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync("user@example.com", "wrong"))
            .ReturnsAsync((string?)null);

        var events = new Mock<IDomainEventPublisher>();
        var sender = CreateSender(ldapServiceMock.Object, events: events.Object);

        await sender.Send(new AuthenticateCommand("user@example.com", "wrong"));

        events.Verify(
            x => x.PublishAsync(
                It.Is<UserAuthenticationFailed>(e => e.Actor == "user@example.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PublishesUserCreated_WithTheNewUsersId()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("create@example.com"))
            .ReturnsAsync((LdapUserInfo?)null);

        var events = new Mock<IDomainEventPublisher>();
        var sender = CreateSender(ldapServiceMock.Object, events: events.Object);

        var created = await sender.Send(new CreateUserCommand("create@example.com", "Create User", "pass"));

        events.Verify(
            x => x.PublishAsync(
                It.Is<UserCreated>(e => e.UserId == created.Id && e.Actor == "create@example.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static LdapUserInfo Alice(string email = "alice@example.com") => new()
    {
        Email = email,
        DisplayName = "Alice",
        Dn = "cn=alice,ou=users,dc=example,dc=com"
    };

    [Fact]
    public async Task GetByIdAsync_ResolvesThroughTheLookupIndex_WithoutWalkingTheDirectory()
    {
        const string email = "alice@example.com";
        var id = DeterministicIdFactory.BuildUserId(email);

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync(email)).ReturnsAsync(Alice());

        var lookup = new Mock<IUserLookupRepository>();
        lookup.Setup(x => x.FindEmailAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);

        var user = await sender.Send(new GetUserByIdQuery(id));

        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        // The whole point of the index: no full enumeration.
        ldapServiceMock.Verify(x => x.GetAllUsersAsync(), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_DropsStaleIndexRow_WhenTheAddressNoLongerYieldsTheSameId()
    {
        // An id that does not derive from the indexed address — what a rename leaves behind.
        var staleId = Guid.NewGuid();

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("renamed@example.com"))
            .ReturnsAsync(Alice("renamed@example.com"));
        ldapServiceMock.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(DirectoryUserListing.Complete([]));

        var lookup = new Mock<IUserLookupRepository>();
        lookup.Setup(x => x.FindEmailAsync(staleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("renamed@example.com");

        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);

        var user = await sender.Send(new GetUserByIdQuery(staleId));

        Assert.Null(user);
        lookup.Verify(x => x.RemoveAsync(staleId, It.IsAny<CancellationToken>()), Times.Once);
        ldapServiceMock.Verify(x => x.GetAllUsersAsync(), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_IndexesTheIdentity_AfterResolvingItByWalking()
    {
        const string email = "alice@example.com";
        var id = DeterministicIdFactory.BuildUserId(email);

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(DirectoryUserListing.Complete([Alice()]));

        // Cold index: FindEmailAsync returns null.
        var lookup = new Mock<IUserLookupRepository>();

        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);

        var user = await sender.Send(new GetUserByIdQuery(id));

        Assert.NotNull(user);
        lookup.Verify(x => x.RecordAsync(id, email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_LeavesTheInstanceOtherCallersHold_Untouched()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(DirectoryUserListing.Complete([Alice()]));
        ldapServiceMock.Setup(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var sender = CreateSender(ldapServiceMock.Object);

        var before = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        var updated = await sender.Send(new UpdateUserCommand(before.Id, "Alice Updated", null, null));

        Assert.NotNull(updated);
        Assert.Equal("Alice Updated", updated.Name);
        Assert.NotSame(before, updated);
        // Previously this assertion failed: the cached DTO was mutated in place, so a
        // concurrent reader saw the new name before the update had finished.
        Assert.Equal("Alice", before.Name);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheIndexRow()
    {
        const string email = "alice@example.com";
        var id = DeterministicIdFactory.BuildUserId(email);

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync(email)).ReturnsAsync(Alice());
        ldapServiceMock.Setup(x => x.DeleteUserAsync(Alice().Dn)).ReturnsAsync(true);

        var lookup = new Mock<IUserLookupRepository>();
        lookup.Setup(x => x.FindEmailAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);

        Assert.True(await sender.Send(new DeleteUserCommand(id)));
        lookup.Verify(x => x.RemoveAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<ILdapService> AliceDirectory()
    {
        var mock = new Mock<ILdapService>();
        mock.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(DirectoryUserListing.Complete([Alice()]));
        mock.Setup(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public async Task UpdateAsync_WritesTheNewAddressToTheDirectory()
    {
        var ldapServiceMock = AliceDirectory();
        var sender = CreateSender(ldapServiceMock.Object);
        var before = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        await sender.Send(new UpdateUserCommand(before.Id, "Alice Updated", "Alice_New@Example.com", null));

        // Previously the new address never left the service: it was applied to the returned
        // DTO and nothing else, so the change vanished when the cache entry expired.
        ldapServiceMock.Verify(
            x => x.UpdateUserAsync(Alice().Dn, "Alice Updated", "alice_new@example.com", null),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTheIdDerivedFromTheNewAddress_AndRetiresTheOldOne()
    {
        var ldapServiceMock = AliceDirectory();
        var lookup = new Mock<IUserLookupRepository>();
        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);
        var before = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        var updated = await sender.Send(new UpdateUserCommand(before.Id, "Alice", "alice_new@example.com", null));

        Assert.NotNull(updated);
        Assert.Equal(DeterministicIdFactory.BuildUserId("alice_new@example.com"), updated.Id);
        Assert.NotEqual(before.Id, updated.Id);
        lookup.Verify(x => x.RemoveAsync(before.Id, It.IsAny<CancellationToken>()), Times.Once);
        lookup.Verify(x => x.RecordAsync(updated.Id, "alice_new@example.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_KeepsTheSameId_WhenTheAddressIsUnchanged()
    {
        var ldapServiceMock = AliceDirectory();
        var lookup = new Mock<IUserLookupRepository>();
        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);
        var before = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        var updated = await sender.Send(new UpdateUserCommand(before.Id, "Alice Updated", null, null));

        Assert.NotNull(updated);
        Assert.Equal(before.Id, updated.Id);
        Assert.Equal("alice@example.com", updated.Email);
        lookup.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RefusesAnAddressAnotherEntryAlreadyAnswersTo()
    {
        // A shared address is a login two entries answer to — the ambiguity a takeover rides on.
        var ldapServiceMock = AliceDirectory();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("admin@example.com"))
            .ReturnsAsync(new LdapUserInfo { Dn = "cn=admin,ou=users,dc=example,dc=com", Email = "admin@example.com" });
        var sender = CreateSender(ldapServiceMock.Object);
        var before = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        await Assert.ThrowsAsync<DomainConflictException>(
            () => sender.Send(new UpdateUserCommand(before.Id, "Alice", "admin@example.com", null)));

        ldapServiceMock.Verify(
            x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAMalformedNewAddress_WithoutTouchingTheDirectory()
    {
        var ldapServiceMock = AliceDirectory();
        var sender = CreateSender(ldapServiceMock.Object);
        var before = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new UpdateUserCommand(before.Id, "Alice", "not-an-address", null)));

        ldapServiceMock.Verify(
            x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessBulkUsersAsync_AppliesNewEmail_OnUpdate()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("update@example.com"))
            .ReturnsAsync(Alice("update@example.com"));
        ldapServiceMock.Setup(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new ProcessBulkUsersCommand(new[]
        {
            new BulkUserRequest
            {
                Operation = "update",
                Email = "update@example.com",
                Name = "Updated",
                NewEmail = "moved@example.com"
            }
        }));

        // BulkUserRequest.NewEmail was declared but read by nothing at all.
        ldapServiceMock.Verify(
            x => x.UpdateUserAsync(Alice().Dn, "Updated", "moved@example.com", null),
            Times.Once);

        var item = response.Items.Single();
        Assert.Equal("completed", item.Status);
        Assert.Equal("moved@example.com", item.User!.Email);
    }

    private static DirectoryUnavailableException Outage() =>
        new("The directory could not be searched for 'x'.");

    [Fact]
    public async Task CreateAsync_DoesNotCreateADuplicate_WhenTheExistenceCheckFails()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("alice@example.com")).ThrowsAsync(Outage());

        var sender = CreateSender(ldapServiceMock.Object);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => sender.Send(new CreateUserCommand("alice@example.com", "Alice", "Password1!")));

        // The whole point: an unreachable directory used to read as "nobody has that address",
        // and the account was created on top of one that already existed.
        ldapServiceMock.Verify(
            x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateUserPatternsAsync_FailsLoudly_RatherThanReportingNoConflicts()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync(It.IsAny<string>())).ThrowsAsync(Outage());

        var sender = CreateSender(ldapServiceMock.Object);

        // Reporting Exists=false for every candidate would tell an operator the whole pool is
        // free and invite them to bulk-create duplicates.
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            sender.Send(new GenerateUserPatternsQuery("node-{01..02}", 10, null, null, null, false, true)));
    }

    [Fact]
    public async Task GetByIdAsync_KeepsTheIndexRow_WhenTheDirectoryIsUnreachable()
    {
        const string email = "alice@example.com";
        var id = DeterministicIdFactory.BuildUserId(email);

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync(email)).ThrowsAsync(Outage());

        var lookup = new Mock<IUserLookupRepository>();
        lookup.Setup(x => x.FindEmailAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var sender = CreateSender(ldapServiceMock.Object, userLookup: lookup.Object);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => sender.Send(new GetUserByIdQuery(id)));

        // An outage is not evidence that the mapping is stale — deleting the row here would
        // throw away a good index every time the directory blinks.
        lookup.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBulkUsersAsync_FailsOnlyTheAffectedItem_WhenTheDirectoryIsUnreachable()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("broken@example.com")).ThrowsAsync(Outage());
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("fine@example.com")).ReturnsAsync((LdapUserInfo?)null);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new ProcessBulkUsersCommand(new[]
        {
            new BulkUserRequest { Email = "broken@example.com", Name = "Broken", Password = "Password1!" },
            new BulkUserRequest { Email = "fine@example.com", Name = "Fine", Password = "Password1!" }
        }));

        Assert.Equal(1, response.FailedCount);
        Assert.Equal(1, response.CompletedCount);
        ldapServiceMock.Verify(x => x.CreateUserAsync("Broken", "broken@example.com", It.IsAny<string>()), Times.Never);
        ldapServiceMock.Verify(x => x.CreateUserAsync("Fine", "fine@example.com", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_IssuesNoToken_WhenGroupMembershipCannotBeDetermined()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Alice().Dn);
        ldapServiceMock.Setup(x => x.GetUserByDnAsync(Alice().Dn))
            .ReturnsAsync(Alice("admin@example.com"));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<LdapUserInfo>())).ThrowsAsync(Outage());

        var tokenService = new Mock<ITokenService>();
        var events = new Mock<IDomainEventPublisher>();
        var sender = CreateSender(ldapServiceMock.Object, events: events.Object, tokenService: tokenService.Object);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            sender.Send(new AuthenticateCommand("admin@example.com", "pass")));

        // Group membership decides the role that gets baked into the JWT. Falling back to the
        // plain User role would hand an administrator a token that silently lacks their
        // authority — and it stays wrong for the whole token lifetime.
        tokenService.Verify(x => x.GenerateToken(It.IsAny<UserDto>()), Times.Never);
        events.Verify(
            x => x.PublishAsync(It.IsAny<UserAuthenticationSucceeded>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_StillIssuesTheUserRole_WhenTheDirectoryAnswersWithNoGroups()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Alice().Dn);
        ldapServiceMock.Setup(x => x.GetUserByDnAsync(Alice().Dn))
            .ReturnsAsync(Alice("nobody@example.com"));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<LdapUserInfo>()))
            .ReturnsAsync(UserRoles.User);

        var sender = CreateSender(ldapServiceMock.Object);

        var response = await sender.Send(new AuthenticateCommand("nobody@example.com", "pass"));

        // A user genuinely in no mapped group is a normal outcome and must keep working —
        // only "we could not find out" is now an error.
        Assert.NotNull(response);
        Assert.Equal(UserRoles.User, response.User.Role);
    }

    [Fact]
    public async Task AuthenticateAsync_DoesNotReportAnOutageAsBadCredentials()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.AuthenticateAsync("admin@example.com", "correct-password"))
            .ThrowsAsync(Outage());

        var tokenService = new Mock<ITokenService>();
        var events = new Mock<IDomainEventPublisher>();
        var sender = CreateSender(ldapServiceMock.Object, events: events.Object, tokenService: tokenService.Object);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            sender.Send(new AuthenticateCommand("admin@example.com", "correct-password")));

        tokenService.Verify(x => x.GenerateToken(It.IsAny<UserDto>()), Times.Never);
        // An unreachable directory is not a failed login attempt. Recording it as one would
        // both mislead whoever reads the audit trail and bury the outage among ordinary typos.
        events.Verify(
            x => x.PublishAsync(It.IsAny<UserAuthenticationFailed>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_SurfacesThatTheEnumerationWasCutShort()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Truncated([Alice()]));

        var sender = CreateSender(ldapServiceMock.Object);

        var listing = await sender.Send(new GetAllUsersQuery());

        Assert.Single(listing.Items);
        // The user returned is real; the list simply is not the whole directory.
        Assert.False(listing.IsComplete);
    }

    [Fact]
    public async Task GetByIdAsync_RefusesToConcludeAbsence_FromATruncatedWalk()
    {
        var missingId = DeterministicIdFactory.BuildUserId("somebody-else@example.com");

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Truncated([Alice()]));

        var sender = CreateSender(ldapServiceMock.Object);

        // Answering 404 here would tell the caller the account does not exist on the strength
        // of a search that stopped early — the user may be in the pages that failed to load.
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => sender.Send(new GetUserByIdQuery(missingId)));
    }

    [Fact]
    public async Task GetByIdAsync_StillReturnsNull_WhenACompleteWalkFindsNothing()
    {
        var missingId = DeterministicIdFactory.BuildUserId("somebody-else@example.com");

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete([Alice()]));

        var sender = CreateSender(ldapServiceMock.Object);

        // A complete walk that found nobody is real evidence of absence, and must stay a 404.
        Assert.Null(await sender.Send(new GetUserByIdQuery(missingId)));
    }

    [Fact]
    public async Task GetByIdAsync_ResolvesNormally_EvenWhenTheWalkWasTruncated()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Truncated([Alice()]));

        var sender = CreateSender(ldapServiceMock.Object);
        var id = DeterministicIdFactory.BuildUserId("alice@example.com");

        // Incompleteness only invalidates a negative answer. Finding the user is still an answer.
        var user = await sender.Send(new GetUserByIdQuery(id));

        Assert.NotNull(user);
        Assert.Equal("alice@example.com", user.Email);
    }
}
