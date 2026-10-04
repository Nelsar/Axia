using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;

namespace Nelsar.Axia.Tests;

public class RateLimitSettingsRepositoryTests
{
    // Avoids AppDbContext's real OnModelCreating: its relational-only ToTable() calls
    // aren't compatible with the EF InMemory provider version used by this test project
    // (see ProviderRepositoryTests/AuditRepositoryTests for the same pattern).
    private sealed class TestRateLimitDbContext : AppDbContext
    {
        public TestRateLimitDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RateLimitSettings>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.PolicyName).IsRequired();
                entity.Property(x => x.PermitLimit).IsRequired();
                entity.Property(x => x.WindowSeconds).IsRequired();
                entity.Property(x => x.QueueLimit).IsRequired();
                entity.Property(x => x.Enabled).IsRequired();
            });
        }
    }

    private static AppDbContext NewDb() =>
        new TestRateLimitDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task UpsertAsync_CreatesRow_WhenPolicyDoesNotExist()
    {
        await using var db = NewDb();
        var repo = new RateLimitSettingsRepository(db);

        var saved = await repo.UpsertAsync(RateLimitSettings.Create("auth-login", 5, 60, 0, true));

        Assert.NotEqual(Guid.Empty, saved.Id);
        var fetched = await repo.GetByPolicyNameAsync("auth-login");
        Assert.NotNull(fetched);
        Assert.Equal(5, fetched!.PermitLimit);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingRow_InsteadOfDuplicating()
    {
        await using var db = NewDb();
        var repo = new RateLimitSettingsRepository(db);

        await repo.UpsertAsync(RateLimitSettings.Create("auth-login", 5, 60, 0, true));
        await repo.UpsertAsync(RateLimitSettings.Create("auth-login", 20, 30, 0, false));

        var all = (await repo.GetAllAsync()).ToList();
        Assert.Single(all);
        Assert.Equal(20, all[0].PermitLimit);
        Assert.Equal(30, all[0].WindowSeconds);
        Assert.False(all[0].Enabled);
    }
}
