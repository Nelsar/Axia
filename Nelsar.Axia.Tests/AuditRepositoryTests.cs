using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The audit page, against SQLite itself.
///
/// It used to load every audit row and sort them in memory on each request, because EF Core will
/// not ORDER BY a DateTimeOffset on SQLite. Since every command and every bulk row writes an
/// entry, that was a full read of the fastest-growing table per page view.
/// </summary>
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

    [Fact]
    public async Task Pages_ComeNewestFirst_AndSayWhetherThereIsMore()
    {
        await using var store = await Store.OpenAsync();

        // Trimmed fractions of different lengths and a whole second: the stored text is not
        // fixed-width, and ordering it as text has to agree with ordering the instants.
        var times = new[]
        {
            At("2026-10-06T08:00:00.0000000Z"),
            At("2026-10-06T08:00:00.5000000Z"),
            At("2026-10-06T08:00:00.4500000Z"),
            At("2026-10-06T08:00:00.0450000Z"),
            At("2026-10-06T08:00:01.0000000Z")
        };
        foreach (var time in times)
            await store.Repository.AddAsync(Entry(time));

        var seen = new List<DateTimeOffset>();
        var offset = 0;
        bool hasMore;
        do
        {
            (var items, hasMore) = await store.Repository.GetRecentAsync(offset, pageSize: 2);
            seen.AddRange(items.Select(e => e.Timestamp));
            offset += 2;
        }
        while (hasMore);

        Assert.Equal(times.OrderByDescending(t => t), seen);
    }

    [Fact]
    public async Task ATimestampWithAnOffset_SortsByTheInstant_NotByItsText()
    {
        await using var store = await Store.OpenAsync();

        // 12:00 at +05:00 is 07:00 UTC — earlier than 08:00 UTC, though "12" sorts after "08".
        var earlier = DateTimeOffset.Parse("2026-10-06T12:00:00+05:00");
        var later = At("2026-10-06T08:00:00Z");
        await store.Repository.AddAsync(Entry(earlier));
        await store.Repository.AddAsync(Entry(later));

        var (items, _) = await store.Repository.GetRecentAsync(0, 10);

        Assert.Equal([later, earlier], items.Select(e => e.Timestamp));
    }

    [Fact]
    public async Task RowsWrittenBeforeTheConverter_ReadBack_AndSortAmongTheNewOnes()
    {
        await using var store = await Store.OpenAsync();

        // Written through the mapping as it was before the converter: EF Core's own DateTimeOffset
        // type, with SQLite choosing the text.
        var old = At("2026-10-06T08:00:00.25Z");
        await using (var legacy = new UnconvertedTimestampContext(store.Options))
        {
            legacy.AuditEvents.Add(Entry(old));
            await legacy.SaveChangesAsync();
        }

        var before = At("2026-10-06T08:00:00.2Z");
        var after = At("2026-10-06T08:00:00.3Z");
        await store.Repository.AddAsync(Entry(before));
        await store.Repository.AddAsync(Entry(after));

        var (items, _) = await store.Repository.GetRecentAsync(0, 10);

        Assert.Equal([after, old, before], items.Select(e => e.Timestamp));
    }

    [Fact]
    public async Task OnlyOnePage_IsRead()
    {
        await using var store = await Store.OpenAsync();
        for (var i = 0; i < 5; i++)
            await store.Repository.AddAsync(Entry(At("2026-10-06T08:00:00Z").AddMinutes(i)));
        store.Commands.Clear();

        await store.Repository.GetRecentAsync(0, 2);

        var query = Assert.Single(store.Commands);
        Assert.Contains("ORDER BY", query);
        Assert.Contains("LIMIT", query);
    }

    [Fact]
    public async Task TheAuditEndpoint_PagesNewestFirst_OverSqlite()
    {
        // The rest of the API tests run on the in-memory provider, which sorts anything; the
        // refusal to ORDER BY a DateTimeOffset is SQLite's, so this one runs on SQLite.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(AppDbContext));
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(DbContextOptions));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var times = Enumerable.Range(0, 5).Select(i => At("2026-10-06T08:00:00Z").AddSeconds(i * 1.5)).ToList();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            db.AuditEvents.AddRange(times.Select(Entry));
            await db.SaveChangesAsync();
        }

        var seen = new List<DateTimeOffset>();
        var url = "/api/v1/audit-events?pageSize=2";
        while (true)
        {
            var page = await client.GetFromJsonAsync<JsonElement>(url);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("timestamp").GetDateTimeOffset()));
            if (!page.GetProperty("hasMore").GetBoolean())
                break;
            url = "/api/v1/audit-events?pageSize=2&pageToken=" + Uri.EscapeDataString(page.GetProperty("pageToken").GetString()!);
        }

        Assert.Equal(times.OrderByDescending(t => t), seen);
    }

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value);

    private static AuditEvent Entry(DateTimeOffset occurredAt) =>
        AuditEvent.Modification(null, null, "admin@example.com", "UpdateUser", true, null, occurredAt);

    /// <summary>The model as it stood before the timestamp had a converter.</summary>
    private sealed class UnconvertedTimestampContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AuditEvent>().Property(e => e.Timestamp).HasConversion((ValueConverter?)null);
        }
    }

    /// <summary>A migrated in-memory database, the repository over it, and the SQL it ran.</summary>
    private sealed class Store : IAsyncDisposable
    {
        private Store(SqliteConnection connection, DbContextOptions<AppDbContext> options, AppDbContext db, List<string> commands)
        {
            Connection = connection;
            Options = options;
            Db = db;
            Commands = commands;
            Repository = new AuditRepository(db);
        }

        public SqliteConnection Connection { get; }
        public DbContextOptions<AppDbContext> Options { get; }
        public AppDbContext Db { get; }
        public List<string> Commands { get; }
        public AuditRepository Repository { get; }

        public static async Task<Store> OpenAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var commands = new List<string>();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .LogTo(line => commands.Add(line), [RelationalEventId.CommandExecuted])
                .Options;

            var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            return new Store(connection, options, db, commands);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
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
