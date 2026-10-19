using System.Globalization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Abstractions;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// What a bulk row reports, which the console shows as it is and the audit trail keeps.
///
/// A failed row used to carry its exception's message whatever the exception was, so a bug or the
/// LDAP library's own error put its wording — host names included — in the response and in the
/// audit trail. And a row that succeeded said so in English whatever language the caller used.
/// </summary>
public class BulkRowMessageTests
{
    private static readonly BulkUserRequest CreateAccount = new()
    {
        Operation = "create",
        Email = "new.user@example.com",
        Name = "New User",
        Password = "P@ssw0rd!"
    };

    private static (ISender Sender, Mock<ILdapService> Ldap, RecordingAuditRepository Audit) Pipeline()
    {
        var ldap = new Mock<ILdapService>();
        var audit = new RecordingAuditRepository();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(ldap.Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.AddSingleton(new Mock<IUserLookupRepository>().Object);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("creator@example.com"));
        services.AddSingleton<IPermissionResolver>(new FixedPermissionResolver(Permissions.UsersBulk, Permissions.UsersCreate));
        services.AddSingleton<IAuditRepository>(audit);
        services.WithAuditTrail();

        var scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider.GetRequiredService<ISender>(), ldap, audit);
    }

    [Fact]
    public async Task ARowThatFailsUnexpectedly_SaysSo_WithoutTheExceptionsText()
    {
        var (sender, ldap, audit) = Pipeline();
        ldap.Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Connection to dc01.corp.local:636 was reset"));

        var result = await sender.Send(new ProcessBulkUsersCommand([CreateAccount]));

        var row = Assert.Single(result.Items);
        Assert.Equal(BulkOperationStatus.Failed, row.Status);
        Assert.Equal(ApplicationMessages.BulkRowFailedUnexpectedly(), row.Message);

        // Nor in the audit trail, which records each row's error.
        Assert.All(audit.Events, e => Assert.DoesNotContain("dc01", e.Details ?? string.Empty));
        Assert.Contains(audit.Events, e => e.Action == "BulkCreateUser" && !e.Success);
    }

    [Fact]
    public async Task ARowRefusedForAReasonWrittenForTheCaller_KeepsThatReason()
    {
        var (sender, ldap, _) = Pipeline();
        ldap.Setup(x => x.GetUserDetailsAsync("new.user@example.com"))
            .ReturnsAsync(new LdapUserInfo { Email = "new.user@example.com", Dn = "uid=new.user,dc=example,dc=com" });

        var result = await sender.Send(new ProcessBulkUsersCommand([CreateAccount]));

        var row = Assert.Single(result.Items);
        Assert.Equal(BulkOperationStatus.Failed, row.Status);
        Assert.Equal(ApplicationMessages.UserAlreadyExists(), row.Message);
    }

    [Fact]
    public async Task ACompletedRow_SaysSoInTheCallersLanguage()
    {
        var (sender, _, _) = Pipeline();

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
        try
        {
            var result = await sender.Send(new ProcessBulkUsersCommand([CreateAccount]));

            var row = Assert.Single(result.Items);
            Assert.Equal(BulkOperationStatus.Completed, row.Status);
            Assert.Equal("Пользователь успешно создан.", row.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
