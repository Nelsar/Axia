using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Which database the application uses, that each database's migrations describe the model, and
/// that a deployment can move from one database to another with nothing left behind. Everything
/// here runs on SQLite; the same, against a real PostgreSQL, is in <see cref="PostgreSqlTests"/>.
/// </summary>
public class DatabaseTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();

    [Fact]
    public void AnUnsetProvider_StopsStartup_SayingWhatToSetAndHowToKeepAnOlderDatabase()
    {
        // The configuration every earlier version ran with: a SQLite file and no provider.
        var ex = Assert.Throws<InvalidOperationException>(() => AxiaDatabase.FromConfiguration(
            Configuration(("ConnectionStrings:DefaultConnection", "Data Source=/data/nelsar-axia.db"))));

        Assert.Contains("Database:Provider is not set", ex.Message);
        Assert.Contains("PostgreSql", ex.Message);
        Assert.Contains("Sqlite", ex.Message);
        Assert.Contains("copy-database", ex.Message);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("7")]
    public void AnUnknownProvider_IsRefused_WithTheChoices(string provider)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AxiaDatabase.FromConfiguration(Configuration(("Database:Provider", provider))));

        Assert.Contains($"'{provider}'", ex.Message);
        Assert.Contains("PostgreSql, Sqlite", ex.Message);
    }

    [Fact]
    public void PostgreSql_HasToBeToldWhereTheServerIs()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AxiaDatabase.FromConfiguration(Configuration(("Database:Provider", "PostgreSql"))));

        Assert.Contains("ConnectionStrings:DefaultConnection is not set", ex.Message);
    }

    [Theory]
    [InlineData("postgresql", DatabaseProvider.PostgreSql)]
    [InlineData("SQLITE", DatabaseProvider.Sqlite)]
    public void TheProvider_IsReadWhateverItsCase(string configured, DatabaseProvider expected)
    {
        var (provider, _) = AxiaDatabase.FromConfiguration(Configuration(
            ("Database:Provider", configured), ("ConnectionStrings:DefaultConnection", "Host=db")));

        Assert.Equal(expected, provider);
    }

    [Fact]
    public void Sqlite_WithoutAConnectionString_UsesTheFileItAlwaysHas()
    {
        var (_, connectionString) = AxiaDatabase.FromConfiguration(Configuration(("Database:Provider", "Sqlite")));

        Assert.Equal("Data Source=nelsar-axia.db", connectionString);
    }

    [Theory]
    [InlineData("Host=db;Database=axia", "GSS Encryption Mode=Disable")]
    [InlineData("Host=db;Database=axia;GSS Encryption Mode=Prefer", "GSS Encryption Mode=Prefer")]
    public void PostgreSql_LeavesGssEncryptionOff_UnlessTheConnectionStringAsksForIt(string configured, string expected)
    {
        // On by default in the driver, which then looks for a Kerberos library the image does not
        // have and says so on every new connection.
        Assert.Contains(expected, AxiaDatabase.WithoutGssUnlessAsked(configured));
    }

    [Theory]
    [InlineData(DatabaseProvider.PostgreSql, "Host=localhost;Database=unused")]
    [InlineData(DatabaseProvider.Sqlite, "Data Source=:memory:")]
    public void EachDatabasesMigrations_DescribeTheModel(DatabaseProvider provider, string connectionString)
    {
        // A change to the model needs a migration for every database. Forgetting one leaves that
        // database a version behind, which nothing else would notice until it was deployed.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseAxiaDatabase(provider, connectionString).Options);

        Assert.False(db.Database.HasPendingModelChanges(),
            $"The model has changed since {provider}'s last migration. Add one to it; see CONTRIBUTING.md.");
    }

    [Fact]
    public void SqlitesMigrations_AreTheOnesEarlierVersionsRecorded()
    {
        // A SQLite database lists the migrations applied to it by these ids. Renaming or dropping
        // one, or moving it where EF Core no longer finds it, makes every existing database look
        // unmigrated, and the next start tries to create tables that are already there.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseAxiaDatabase(DatabaseProvider.Sqlite, "Data Source=:memory:").Options);

        Assert.Equal(new[]
        {
            "20260426102226_AddLdapConfigurationStorage",
            "20260712010000_AddAuditAndProviders",
            "20260719130047_AddProviderLdapConnectionDetails",
            "20260719133446_AddRoleMappings",
            "20260719134021_AddRateLimitSettings",
            "20260719140457_UpdateDefaultUserSearchFilter",
            "20260726100312_RenameLdapConfigurationNamespace",
            "20260726110453_EfCore10AlignmentCheck",
            "20260801171206_AddUserLookupIndex",
            "20260801181238_EnforceProviderPortRange",
            "20260802091100_AddRolePermissions",
            "20261005101540_RevokeAccidentalDefaultGrants",
            "20261006060417_IndexAuditEventsByTimestamp",
            "20261007071503_AddProviderSecurity",
        }, db.Database.GetMigrations().Take(14));
    }

    [Fact]
    public async Task Copy_CarriesEveryColumnOfEveryRow()
    {
        await using var from = await SqliteDatabase.OpenAsync();
        await using var to = await SqliteDatabase.OpenAsync(migrate: false);

        // More audit rows than one batch, so the paging is part of what is checked.
        await using (var db = from.Context())
            await DatabaseContent.FillEveryTableAsync(db, auditRows: 1201);

        var copied = await DatabaseCopier.CopyAsync(from.Options, to.Options, NullLogger.Instance);

        await using var source = from.Context();
        await using var target = to.Context();
        var expected = await DatabaseContent.ReadAsync(source);
        DatabaseContent.AssertSame(expected, await DatabaseContent.ReadAsync(target));

        Assert.Equal(expected.Select(t => (t.Key, t.Value.Count)).Order(), copied.Select(t => (t.Table, t.Rows)).Order());
        Assert.Equal(1201, copied.Single(t => t.Table == "AuditEvents").Rows);
    }

    [Fact]
    public async Task Copy_RefusesATargetThatAlreadyHasRows()
    {
        await using var from = await SqliteDatabase.OpenAsync();
        await using var to = await SqliteDatabase.OpenAsync();
        await using (var db = from.Context())
            await DatabaseContent.FillEveryTableAsync(db);
        await using (var db = to.Context())
            await DatabaseContent.FillEveryTableAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseCopier.CopyAsync(from.Options, to.Options, NullLogger.Instance));

        Assert.Contains("already has rows", ex.Message);
    }

    [Fact]
    public async Task Copy_RefusesASourceOlderThanThisVersion()
    {
        await using var from = await SqliteDatabase.OpenAsync(migrate: false);
        await using var to = await SqliteDatabase.OpenAsync(migrate: false);
        await using (var db = from.Context())
            await db.GetService<IMigrator>().MigrateAsync("20261006060417_IndexAuditEventsByTimestamp");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseCopier.CopyAsync(from.Options, to.Options, NullLogger.Instance));

        Assert.Contains("older than this version", ex.Message);
        Assert.Contains("20261007071503_AddProviderSecurity", ex.Message);
    }

    [Fact]
    public async Task Copy_RefusesASourceThatHoldsNoDatabaseOfOurs()
    {
        await using var from = await SqliteDatabase.OpenAsync(migrate: false);
        await using var to = await SqliteDatabase.OpenAsync(migrate: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseCopier.CopyAsync(from.Options, to.Options, NullLogger.Instance));

        Assert.Contains("--from", ex.Message);
    }

    [Fact]
    public void AReadOnlySource_CannotBeCreatedByMistake()
    {
        // A mistyped path must not leave a new, empty database where the old one was expected.
        var path = Path.Join(Path.GetTempPath(), $"axia-missing-{Guid.NewGuid():N}.db");
        using var connection = new SqliteConnection(AxiaDatabase.ReadOnlySqlite($"Data Source={path}"));

        Assert.Throws<SqliteException>(connection.Open);
        Assert.False(File.Exists(path));
    }

    /// <summary>A SQLite database in memory, alive for as long as this holds its connection.</summary>
    private sealed class SqliteDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqliteDatabase(SqliteConnection connection)
        {
            _connection = connection;
            Options = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(connection).Options;
        }

        public DbContextOptions<AppDbContext> Options { get; }

        public AppDbContext Context() => new(Options);

        public static async Task<SqliteDatabase> OpenAsync(bool migrate = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new SqliteDatabase(connection);

            if (migrate)
            {
                await using var db = database.Context();
                await db.Database.MigrateAsync();
            }

            return database;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
