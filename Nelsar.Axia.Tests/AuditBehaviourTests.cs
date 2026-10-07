using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Features.Groups.Commands;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Every change is in the audit trail, under the name of whoever made it, whether or not it was
/// allowed.
///
/// The trail used to record four things: sign-in success and failure, account creation and
/// account update — the last two with the changed account in the actor column. Deletions, bulk
/// batches and the passwords they reset, groups, units, computers, connection profiles, role
/// mappings and rate limits left nothing behind.
/// </summary>
public class AuditBehaviourTests
{
    [Fact]
    public void EveryCommand_SaysHowItAppearsInTheTrail()
    {
        // Sign-in is recorded by its own events, as an authentication rather than a change.
        var commands = typeof(DeleteUserCommand).Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Command", StringComparison.Ordinal)
                && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Where(type => type != typeof(AuthenticateCommand))
            .ToList();

        Assert.NotEmpty(commands);
        Assert.All(commands, type => Assert.True(typeof(IAuditedCommand).IsAssignableFrom(type),
            $"{type.Name} changes something but is not an IAuditedCommand, so it would leave no trace."));
    }

    private static (ISender Sender, RecordingAuditRepository Trail, Mock<ILdapService> Ldap) Pipeline(
        ICurrentUser currentUser,
        IPermissionResolver permissions)
    {
        var trail = new RecordingAuditRepository();
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(DirectoryUserListing.Complete([]));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(new Mock<Nelsar.Axia.Domain.Repositories.IUserLookupRepository>().Object);
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.AddSingleton(currentUser);
        services.AddSingleton(permissions);
        services.AddSingleton<Nelsar.Axia.Domain.Repositories.IAuditRepository>(trail);
        services.WithNoRoleMappings();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), trail, ldap);
    }

    [Fact]
    public async Task ARefusedAttempt_IsRecorded_WithWhoTriedIt()
    {
        // The case the trail matters most for: someone without groups.manage trying to rewrite a
        // group's membership — the way to make oneself an administrator.
        var (sender, trail, _) = Pipeline(
            new TestCurrentUser("employee@example.com"),
            new FixedPermissionResolver(Permissions.GroupsRead));
        var groupId = Guid.NewGuid();

        await Assert.ThrowsAsync<PermissionDeniedException>(() => sender.Send(new UpdateGroupCommand(
            groupId, null, null, null, ["uid=employee,ou=people,dc=example,dc=com"])));

        var entry = Assert.Single(trail.Events);
        Assert.Equal("employee@example.com", entry.Actor);
        Assert.Equal("UpdateGroup", entry.Action);
        Assert.Equal(groupId, entry.ObjectId);
        Assert.False(entry.Success);
        Assert.Contains("members replaced with uid=employee,ou=people,dc=example,dc=com", entry.Details);
        Assert.Contains(nameof(PermissionDeniedException), entry.Details);
    }

    [Fact]
    public async Task ADeletion_IsRecorded_WithTheActorRatherThanTheTarget()
    {
        var (sender, trail, _) = Pipeline(
            new TestCurrentUser("deleter@example.com", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new FixedPermissionResolver([.. Permissions.All]));
        var userId = Guid.NewGuid();

        // No such user: the use case answers false, and the trail says nothing was deleted.
        await sender.Send(new DeleteUserCommand(userId));

        var entry = Assert.Single(trail.Events);
        Assert.Equal("deleter@example.com", entry.Actor);
        Assert.Equal("DeleteUser", entry.Action);
        Assert.Equal(userId, entry.ObjectId);
        Assert.False(entry.Success);
        Assert.Contains("directory=11111111-1111-1111-1111-111111111111", entry.Details);
        Assert.Contains("not found", entry.Details);
    }

    [Fact]
    public async Task ABulkPasswordReset_IsRecordedPerRow()
    {
        var (sender, trail, ldap) = Pipeline(
            new TestCurrentUser("editor@example.com"),
            new FixedPermissionResolver(Permissions.UsersBulk, Permissions.UsersUpdate));
        ldap.Setup(x => x.GetUserDetailsAsync("admin@example.com"))
            .ReturnsAsync(new LdapUserInfo { Email = "admin@example.com", Dn = "uid=admin,dc=example,dc=com" });

        await sender.Send(new ProcessBulkUsersCommand(
        [
            new BulkUserRequest { Operation = "update", Email = "admin@example.com", Name = "Admin", Password = "N3w-Pa55!" }
        ]));

        var row = Assert.Single(trail.Events, e => e.Action == "BulkUpdateUser");
        Assert.Equal("editor@example.com", row.Actor);
        Assert.Equal("uid=admin,dc=example,dc=com", row.ObjectDn);
        Assert.True(row.Success);
        Assert.Contains("password reset", row.Details);
        Assert.DoesNotContain("N3w-Pa55!", row.Details);

        // And the batch as a whole, once.
        Assert.Single(trail.Events, e => e.Action == "ProcessBulkUsers" && e.Success);
    }

    [Fact]
    public async Task APasswordIsNeverWrittenToTheTrail()
    {
        var (sender, trail, ldap) = Pipeline(
            new TestCurrentUser("admin@example.com"),
            new FixedPermissionResolver([.. Permissions.All]));
        ldap.Setup(x => x.GetUserDetailsAsync(It.IsAny<string>())).ReturnsAsync((LdapUserInfo?)null);

        await sender.Send(new CreateUserCommand("new.user@example.com", "New User", "Sup3r-Secret!"));

        Assert.NotEmpty(trail.Events);
        Assert.All(trail.Events, e => Assert.DoesNotContain("Sup3r-Secret!", e.Details ?? string.Empty));
    }
}
