using Moq;
using Nelsar.Axia.Application.EventHandlers;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Events;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Pins the mapping from sign-in events onto audit rows. Changes are audited by AuditBehaviour
/// instead; see AuditBehaviourTests.
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
}
