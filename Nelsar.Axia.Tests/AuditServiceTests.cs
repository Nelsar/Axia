using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Nelsar.Axia.Tests
{
    public class AuditServiceTests
    {
        [Fact]
        public async Task LogAuthenticationAsync_CallsRepository_WithCorrectEvent()
        {
            var repoMock = new Mock<IAuditRepository>();
            AuditEvent? captured = null;
            repoMock.Setup(r => r.AddAsync(It.IsAny<AuditEvent>()))
                .Returns<AuditEvent>(evt => { captured = evt; return Task.CompletedTask; });

            var logger = new NullLogger<AuditService>();
            var svc = new AuditService(repoMock.Object, logger);

            var dn = "cn=alice,ou=users,dc=example,dc=com";
            await svc.LogAuthenticationAsync("alice@example.com", true, dn, "ok");

            repoMock.Verify(r => r.AddAsync(It.IsAny<AuditEvent>()), Times.Once);
            Assert.NotNull(captured);
            Assert.Equal("Authentication", captured.EventType);
            Assert.Equal("alice@example.com", captured.Actor);
            Assert.True(captured.Success);
            Assert.Equal(dn, captured.ObjectDn);

            // verify deterministic GUID matches SHA256->GUID used in implementation
            var expected = ComputeGuidFromString(dn);
            Assert.Equal(expected, captured.ObjectId);
        }

        [Fact]
        public async Task LogModificationAsync_ComputesObjectId_WhenObjectIdMissing()
        {
            var repoMock = new Mock<IAuditRepository>();
            AuditEvent? captured = null;
            repoMock.Setup(r => r.AddAsync(It.IsAny<AuditEvent>()))
                .Returns<AuditEvent>(evt => { captured = evt; return Task.CompletedTask; });

            var logger = new NullLogger<AuditService>();
            var svc = new AuditService(repoMock.Object, logger);

            var dn = "cn=bob,ou=users,dc=example,dc=com";
            await svc.LogModificationAsync(null, dn, "bob@example.com", "UpdateUser", true, "changed name");

            repoMock.Verify(r => r.AddAsync(It.IsAny<AuditEvent>()), Times.Once);
            Assert.NotNull(captured);
            Assert.Equal("Modification", captured.EventType);
            Assert.Equal("UpdateUser", captured.Action);
            Assert.Equal("bob@example.com", captured.Actor);
            Assert.Equal(dn, captured.ObjectDn);
            Assert.True(captured.Success);

            var expected = ComputeGuidFromString(dn);
            Assert.Equal(expected, captured.ObjectId);
        }

        [Fact]
        public async Task LogAuthenticationAsync_DoesNotThrow_WhenRepositoryThrows()
        {
            var repoMock = new Mock<IAuditRepository>();
            repoMock.Setup(r => r.AddAsync(It.IsAny<AuditEvent>())).ThrowsAsync(new InvalidOperationException("db error"));

            var logger = new NullLogger<AuditService>();
            var svc = new AuditService(repoMock.Object, logger);

            // should swallow exception and not throw
            await svc.LogAuthenticationAsync("eve@example.com", false, null, "fail");

            repoMock.Verify(r => r.AddAsync(It.IsAny<AuditEvent>()), Times.Once);
        }

        private static Guid ComputeGuidFromString(string input)
        {
            var normalized = input.Trim().ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
