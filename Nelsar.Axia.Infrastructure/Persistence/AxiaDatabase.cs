using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Nelsar.Axia.Infrastructure.Persistence;

/// <summary>The databases the application can keep its own state in.</summary>
public enum DatabaseProvider
{
    PostgreSql,
    Sqlite,
}

/// <summary>
/// Which database the application uses, and the one place that turns that choice into EF Core
/// options.
///
/// Each database has its migrations in an assembly of its own, named here: a migration is SQL for
/// one engine, and EF Core keeps one history per assembly. Supporting another database is a third
/// <see cref="DatabaseProvider"/> value, its EF Core package, a migrations project created the way
/// CONTRIBUTING.md describes, and a line in <see cref="UseAxiaDatabase{TContext}"/>; nothing
/// outside this file chooses an engine.
/// </summary>
public static class AxiaDatabase
{
    public const string ProviderKey = "Database:Provider";
    public const string ConnectionStringName = "DefaultConnection";

    public const string PostgreSqlMigrationsAssembly = "Nelsar.Axia.Migrations.PostgreSql";
    public const string SqliteMigrationsAssembly = "Nelsar.Axia.Migrations.Sqlite";

    /// <summary>What SQLite has always defaulted to: a file in the working directory.</summary>
    public const string SqliteDefaultConnectionString = "Data Source=nelsar-axia.db";

    /// <summary>Points the options at a database, with that database's migrations.</summary>
    public static DbContextOptionsBuilder<TContext> UseAxiaDatabase<TContext>(
        this DbContextOptionsBuilder<TContext> options,
        DatabaseProvider provider,
        string connectionString)
        where TContext : DbContext
    {
        Configure(options, provider, connectionString);
        return options;
    }

    /// <inheritdoc cref="UseAxiaDatabase{TContext}"/>
    public static DbContextOptionsBuilder UseAxiaDatabase(
        this DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString) =>
        Configure(options, provider, connectionString);

    // Not an extension method, so that the generic overload above cannot end up calling itself.
    private static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case DatabaseProvider.PostgreSql:
                options.UseNpgsql(WithoutGssUnlessAsked(connectionString),
                    npgsql => npgsql.MigrationsAssembly(PostgreSqlMigrationsAssembly));
                break;
            case DatabaseProvider.Sqlite:
                options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(SqliteMigrationsAssembly));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }

        return options;
    }

    /// <summary>
    /// SQLite over a connection the caller holds open — how a test keeps an in-memory database
    /// alive for as long as it needs it — with SQLite's migrations.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseAxiaSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> options,
        DbConnection connection)
        where TContext : DbContext
    {
        options.UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(SqliteMigrationsAssembly));
        return options;
    }

    /// <summary>
    /// Reads <c>Database:Provider</c> and <c>ConnectionStrings:DefaultConnection</c>, or says what
    /// to set.
    ///
    /// There is no default provider. Every version before 0.3 used SQLite without being told to,
    /// so a configuration written for one of them names a SQLite file and no provider; defaulting
    /// to PostgreSQL would hand that file name to the PostgreSQL driver, which reads
    /// <c>Data Source</c> as a host and fails somewhere far from this setting. Defaulting to
    /// SQLite would keep such a deployment on SQLite without anyone having decided it.
    /// </summary>
    public static (DatabaseProvider Provider, string ConnectionString) FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ProviderKey} is not set. Set it to PostgreSql, with ConnectionStrings:{ConnectionStringName} naming the "
                + "server (Host=…;Database=…;Username=…;Password=…), or to Sqlite to keep the application's state in a "
                + "SQLite file. A deployment upgraded from 0.2 or earlier has a SQLite database: set Sqlite to go on "
                + "using it, or copy it into PostgreSQL with the copy-database command first (see docs/operations.md).");
        }

        if (!Enum.TryParse<DatabaseProvider>(configured, ignoreCase: true, out var provider)
            || !Enum.IsDefined(provider))
        {
            throw new InvalidOperationException(
                $"{ProviderKey} is '{configured}'. It has to be one of: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");
        }

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (provider != DatabaseProvider.Sqlite)
            {
                throw new InvalidOperationException(
                    $"{ProviderKey} is {provider}, and ConnectionStrings:{ConnectionStringName} is not set. Name the "
                    + "server there: Host=…;Database=…;Username=…;Password=….");
            }

            connectionString = SqliteDefaultConnectionString;
        }

        return (provider, connectionString);
    }

    /// <summary>
    /// GSS encryption off unless the connection string says otherwise.
    ///
    /// The driver tries it first by default, and that means loading the Kerberos library, which
    /// the API's image does not carry: every new connection printed "Cannot load library
    /// libgssapi_krb5.so.2" before falling back to what it would have done anyway. A deployment
    /// that does use GSS names it — <c>GSS Encryption Mode=Prefer</c> — on a host that has the
    /// library, and is left alone.
    /// </summary>
    internal static string WithoutGssUnlessAsked(string connectionString) =>
        connectionString.Contains("gss", StringComparison.OrdinalIgnoreCase)
            ? connectionString
            : new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }.ConnectionString;
}
