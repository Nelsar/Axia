using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Nelsar.Axia.Tests
{
    public class AuditRepositoryTests
    {
        [Fact]
        public async Task AddAsync_PersistsEvent_ToInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: "audit_repo_test_db_" + Guid.NewGuid())
                .Options;

            // Use a test-specific DbContext that avoids relational ToTable() calls
            await using var db = new TestAppDbContext(options);
            var repo = new AuditRepository(db);

            var evt = AuditEvent.Modification(
                objectId: null,
                objectDn: null,
                actor: "tester",
                action: "Create",
                success: true,
                details: "unit test",
                occurredAt: DateTimeOffset.UtcNow);

            await repo.AddAsync(evt);

            var stored = db.AuditEvents.ToList();
            Assert.Single(stored);
            var item = stored.First();
            Assert.Equal(evt.EventType, item.EventType);
            Assert.Equal(evt.Actor, item.Actor);
            Assert.Equal(evt.Action, item.Action);
            Assert.True(item.Timestamp <= DateTimeOffset.UtcNow);
        }
    }

    // Test-only DbContext that keeps model simple to avoid relational-only APIs in tests
    internal class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditEvent>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.EventType).IsRequired().HasMaxLength(128);
                entity.Property(x => x.Actor).IsRequired().HasMaxLength(256);
                entity.Property(x => x.Action).IsRequired().HasMaxLength(256);
                entity.Property(x => x.Details).HasMaxLength(4000);
                entity.Property(x => x.ObjectDn).HasMaxLength(1024);
                entity.Property(x => x.Timestamp).IsRequired();
            });
        }
    }
}
