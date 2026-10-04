using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Tests;

public class ProviderRepositoryTests
{
    private sealed class TestProviderDbContext : AppDbContext
    {
        public TestProviderDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Provider>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired();
                entity.Property(x => x.Host).IsRequired();
                entity.Property(x => x.Port).IsRequired();
                entity.Property(x => x.ProviderType).IsRequired();
                entity.Property(x => x.IsActive).IsRequired();
            });
        }
    }

    [Fact]
        public async Task AddAndGetProvider_Works_WithInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new TestProviderDbContext(options);
        var repo = new ProviderRepository(db);

        var provider = TestProvider.Create(name: "p1", host: "ldap://127.0.0.1");
        var created = await repo.AddAsync(provider);

        Assert.NotEqual(Guid.Empty, created.Id);

        var all = (await repo.GetAllAsync()).ToList();
        Assert.Single(all);
        Assert.Equal("p1", all[0].Name);

        var fetched = await repo.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("p1", fetched!.Name);
        }
}
