using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A bulk batch may do only what its submitter could do one account at a time.
///
/// users.bulk used to be the whole check, and every signed-in user held it. An "update" row sets
/// the account's password, so a single request — <c>[{"operation":"update","email":"admin@…",
/// "password":"…"}]</c> — let any employee take over any account.
/// </summary>
public class BulkPermissionTests
{
    private static readonly BulkUserRequest ResetAdminPassword = new()
    {
        Operation = "update",
        Email = "admin@example.com",
        Name = "Admin",
        Password = "taken-over"
    };

    private static readonly BulkUserRequest CreateAccount = new()
    {
        Operation = "create",
        Email = "new.user@example.com",
        Name = "New User",
        Password = "P@ssw0rd!"
    };

    private static (ISender Sender, Mock<ILdapService> Ldap, Mock<IBulkJobQueue> Queue) Pipeline(
        ICurrentUser currentUser,
        IPermissionResolver permissions,
        Action<IServiceProvider>? beforeSend = null)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetUserDetailsAsync("admin@example.com"))
            .ReturnsAsync(new LdapUserInfo { Email = "admin@example.com", Dn = "uid=admin,dc=example,dc=com" });

        var queue = new Mock<IBulkJobQueue>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(queue.Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.AddSingleton(new Mock<Nelsar.Axia.Domain.Repositories.IUserLookupRepository>().Object);
        services.AddSingleton(currentUser);
        services.AddSingleton(permissions);
        services.WithAuditTrail();

        var scope = services.BuildServiceProvider().CreateScope();
        beforeSend?.Invoke(scope.ServiceProvider);

        return (scope.ServiceProvider.GetRequiredService<ISender>(), ldap, queue);
    }

    [Fact]
    public async Task BulkAlone_CannotResetAPassword()
    {
        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser("employee@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new ProcessBulkUsersCommand([ResetAdminPassword])));

        ldap.Verify(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task BulkAlone_CannotCreateAnAccount()
    {
        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser("employee@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new ProcessBulkUsersCommand([CreateAccount])));

        ldap.Verify(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AMixedBatch_NeedsEveryPermissionItsRowsUse()
    {
        // Holding users.update is not enough for a batch that also creates: the whole batch is
        // refused, rather than the rows the caller may perform going through on their own.
        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser("editor@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk, Permissions.UsersUpdate));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new ProcessBulkUsersCommand([ResetAdminPassword, CreateAccount])));

        ldap.Verify(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateRows_GoThrough_ForSomeoneWhoMayUpdateAccounts()
    {
        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser("editor@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk, Permissions.UsersUpdate));

        var result = await sender.Send(new ProcessBulkUsersCommand([ResetAdminPassword]));

        Assert.Equal(BulkOperationStatus.Completed, Assert.Single(result.Items).Status);

        // Addressed by the DN the row resolved to, not the address it was looked up by.
        ldap.Verify(x => x.UpdateUserAsync("uid=admin,dc=example,dc=com", "Admin", null, "taken-over"), Times.Once);
    }

    [Fact]
    public async Task UpdateRows_AreRefused_ForAnAccountThatCanDoMoreThanTheCaller()
    {
        // Holding users.update used to make every administrator's password one row away. The row
        // naming an account that holds more than the editor fails; the account is not touched.
        const string editorDn = "uid=editor,dc=example,dc=com";
        var resolver = new Mock<IPermissionResolver>();
        resolver.Setup(x => x.ResolveAsync(editorDn, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { Permissions.UsersBulk, Permissions.UsersUpdate });
        resolver.Setup(x => x.ResolveForEntryAsync("uid=admin,dc=example,dc=com", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Permissions.All.ToHashSet());

        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser("editor@example.com", distinguishedName: editorDn),
            resolver.Object);

        var result = await sender.Send(new ProcessBulkUsersCommand([ResetAdminPassword]));

        Assert.Equal(BulkOperationStatus.Failed, Assert.Single(result.Items).Status);
        ldap.Verify(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Queueing_IsRefused_ForRowsTheCallerCannotPerform()
    {
        var (sender, _, queue) = Pipeline(
            new TestCurrentUser("employee@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new EnqueueBulkUserJobCommand([ResetAdminPassword])));

        queue.Verify(x => x.EnqueueAsync(It.IsAny<BulkUserJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownOperation_FailsAsARow_InsteadOfCreatingTheAccount()
    {
        // Every operation other than "update" used to be read as create, so a sheet asking to
        // "delete" a row created the account.
        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser("creator@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk, Permissions.UsersCreate));

        var result = await sender.Send(new ProcessBulkUsersCommand(
        [
            new BulkUserRequest { Operation = "delete", Email = "someone@example.com", Name = "Someone", Password = "x" }
        ]));

        Assert.Equal(BulkOperationStatus.Failed, Assert.Single(result.Items).Status);
        ldap.Verify(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task QueuedWork_IsCheckedAsThePersonWhoQueuedIt()
    {
        // The worker has no request, so the host's principal is empty there. The job runs as the
        // person recorded on it — and is refused if they cannot perform its rows (any more).
        var asked = new List<string>();
        var resolver = new Mock<IPermissionResolver>();
        resolver.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid?, CancellationToken>((entry, _, _) => asked.Add(entry))
            .ReturnsAsync(new HashSet<string> { Permissions.UsersBulk });

        const string employeeDn = "uid=employee,dc=example,dc=com";
        var (sender, ldap, _) = Pipeline(
            new TestCurrentUser(subject: null),
            resolver.Object,
            sp => sp.GetRequiredService<BackgroundPrincipal>().RunAs("employee@example.com", employeeDn, null));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => sender.Send(new ProcessBulkUsersCommand([ResetAdminPassword])));

        // Resolved from the entry recorded on the job, not by searching for the login.
        Assert.Equal(employeeDn, Assert.Single(asked));
        ldap.Verify(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task QueuedWork_WithNobodyBehindIt_IsRefused()
    {
        var (sender, _, _) = Pipeline(
            new TestCurrentUser(subject: null),
            new FixedPermissionResolver([.. Permissions.All]),
            sp => sp.GetRequiredService<BackgroundPrincipal>().RunAs(null, null, null));

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => sender.Send(new ProcessBulkUsersCommand([CreateAccount])));
    }
}
