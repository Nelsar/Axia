using Moq;
using Nelsar.Axia.Application.EventHandlers;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Pins the mapping from domain events onto audit rows — the behaviour that used to be
/// inlined in UserService as TryAuditAsync calls.
/// </summary>
public class AuditTrailEventHandlerTests
{
    [Fact]
    public async Task UserAuthenticationSucceeded_IsRecordedAsSuccessfulAuthentication()
    {
        var auditService = new Mock<IAuditService>();
        var handler = new AuditTrailEventHandler(auditService.Object);

        await handler.HandleAsync(new UserAuthenticationSucceeded("user@example.com", "uid=user,ou=people,dc=example,dc=com"));

        auditService.Verify(
            x => x.LogAuthenticationAsync("user@example.com", true, "uid=user,ou=people,dc=example,dc=com", null),
            Times.Once);
    }

    [Fact]
    public async Task UserAuthenticationFailed_IsRecordedWithReasonAndNoDn()
    {
        var auditService = new Mock<IAuditService>();
        var handler = new AuditTrailEventHandler(auditService.Object);

        await handler.HandleAsync(new UserAuthenticationFailed("user@example.com", "Invalid credentials"));

        auditService.Verify(
            x => x.LogAuthenticationAsync("user@example.com", false, null, "Invalid credentials"),
            Times.Once);
    }

    [Fact]
    public async Task UserCreated_IsRecordedAsCreateUserModification()
    {
        var userId = Guid.NewGuid();
        var auditService = new Mock<IAuditService>();
        var handler = new AuditTrailEventHandler(auditService.Object);

        await handler.HandleAsync(new UserCreated(userId, "admin@example.com"));

        auditService.Verify(
            x => x.LogModificationAsync(userId, null, "admin@example.com", "CreateUser", true, null),
            Times.Once);
    }

    [Fact]
    public async Task UserUpdated_IsRecordedAsUpdateUserModification()
    {
        var userId = Guid.NewGuid();
        var auditService = new Mock<IAuditService>();
        var handler = new AuditTrailEventHandler(auditService.Object);

        await handler.HandleAsync(new UserUpdated(userId, "admin@example.com"));

        auditService.Verify(
            x => x.LogModificationAsync(userId, null, "admin@example.com", "UpdateUser", true, null),
            Times.Once);
    }
}
