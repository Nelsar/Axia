using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Npgsql;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A test that needs a PostgreSQL server. <c>AXIA_TEST_POSTGRES</c> names one, as a connection
/// string for an account that may create databases; each test makes its own and drops it.
///
/// Without it these are skipped, so <c>dotnet test</c> on a laptop still needs nothing but the
/// SDK. CI's API job runs a server and also sets <c>AXIA_TEST_POSTGRES_REQUIRED</c>, which turns
/// a missing server into failures rather than skips: a job that quietly stopped running these
/// would look exactly like one that passed them.
/// </summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public const string Server = "AXIA_TEST_POSTGRES";

    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Server))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Server + "_REQUIRED")))
        {
            Skip = $"Set {Server} to a PostgreSQL connection string that may create databases.";
        }
    }
}

/// <summary>
/// What only a real PostgreSQL can show: that its migrations build the schema, that the audit
/// page orders by its timestamps, and that a database moves between it and SQLite in both
/// directions with every column intact.
/// </summary>
public class PostgreSqlTests
{
    [PostgreSqlFact]
    public async Task TheAuditPage_ReadsNewestFirst_WhateverOffsetEachEntryWasWrittenWith()
    {
        await using var server = await TemporaryDatabase.CreateAsync();
        await using var db = server.Context();
        await db.Database.MigrateAsync();

        // The same instants the SQLite tests use, written with offsets that differ from each
        // other: the driver refuses anything but UTC, and the model converts on the way in.
        var noon = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var earlier = AuditEvent.Authentication("a", true, null, null, null, noon.ToOffset(TimeSpan.FromHours(5)).AddMinutes(-30));
        var latest = AuditEvent.Authentication("b", true, null, null, null, noon.ToOffset(TimeSpan.FromHours(-7)));
        var middle = AuditEvent.Authentication("c", true, null, null, null, noon.AddMinutes(-10));
        db.AuditEvents.AddRange(earlier, latest, middle);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (items, hasMore) = await new AuditRepository(db).GetRecentAsync(0, 2);

        Assert.Equal(["b", "c"], items.Select(e => e.Actor));
        Assert.True(hasMore);
        Assert.Equal(noon, items[0].Timestamp);
    }

    [PostgreSqlFact]
    public async Task ADatabase_MovesFromSqliteToPostgreSql_AndBack_WithEveryColumn()
    {
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        var fromSqlite = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(sqlite).Options;
        await using (var db = new AppDbContext(fromSqlite))
        {
            await db.Database.MigrateAsync();
            await DatabaseContent.FillEveryTableAsync(db, auditRows: 1201);
        }

        await using var server = await TemporaryDatabase.CreateAsync();
        await DatabaseCopier.CopyAsync(fromSqlite, server.Options, NullLogger.Instance);

        await using var back = new SqliteConnection("Data Source=:memory:");
        await back.OpenAsync();
        var intoSqlite = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(back).Options;
        await DatabaseCopier.CopyAsync(server.Options, intoSqlite, NullLogger.Instance);

        await using var original = new AppDbContext(fromSqlite);
        await using var onPostgreSql = server.Context();
        await using var returned = new AppDbContext(intoSqlite);
        var expected = await DatabaseContent.ReadAsync(original);
        DatabaseContent.AssertSame(expected, await DatabaseContent.ReadAsync(onPostgreSql));
        DatabaseContent.AssertSame(expected, await DatabaseContent.ReadAsync(returned));
    }

    /// <summary>A database of its own on the test server, dropped afterwards.</summary>
    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _server;
        private readonly string _name;

        private TemporaryDatabase(string server, string name, string connectionString)
        {
            _server = server;
            _name = name;
            Options = new DbContextOptionsBuilder<AppDbContext>()
                .UseAxiaDatabase(DatabaseProvider.PostgreSql, connectionString).Options;
        }

        public DbContextOptions<AppDbContext> Options { get; }

        public AppDbContext Context() => new(Options);

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            var server = Environment.GetEnvironmentVariable(PostgreSqlFactAttribute.Server)
                ?? throw new InvalidOperationException($"{PostgreSqlFactAttribute.Server} is not set.");
            var name = $"axia_test_{Guid.NewGuid():N}";

            await ExecuteAsync(server, $"CREATE DATABASE \"{name}\"");
            var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
            return new TemporaryDatabase(server, name, connectionString);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(_server, $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)");
        }

        private static async Task ExecuteAsync(string server, string sql)
        {
            await using var connection = new NpgsqlConnection(server);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
