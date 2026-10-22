using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Infrastructure.Persistence;
using Serilog;
using Serilog.Extensions.Logging;

namespace Nelsar.Axia.Api.Configuration;

/// <summary>
/// <c>dotnet Nelsar.Axia.Api.dll copy-database --from-provider Sqlite --from "Data Source=…"</c>
///
/// Copies another database into the one this instance is configured with — Database:Provider and
/// ConnectionStrings:DefaultConnection, read exactly as the API reads them — and exits without
/// starting the API. Configured the same way the API is, so whoever is moving a deployment
/// configures the new database once, copies, and starts. See <see cref="DatabaseCopier"/> for
/// what it refuses and why, and docs/operations.md for the steps.
/// </summary>
internal static class CopyDatabaseCommand
{
    private const string Name = "copy-database";

    public static bool IsInvoked(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(string[] args)
    {
        var rest = args.Skip(1).ToList();
        var fromProvider = Take(rest, "--from-provider");
        var from = Take(rest, "--from");

        if (fromProvider is null || from is null
            || !Enum.TryParse<DatabaseProvider>(fromProvider, ignoreCase: true, out var sourceProvider)
            || !Enum.IsDefined(sourceProvider))
        {
            Log.Error(
                "Usage: {Command} --from-provider {Providers} --from \"<connection string>\". The database copied into "
                + "is the configured one: Database:Provider and ConnectionStrings:DefaultConnection.",
                Name, string.Join(" | ", Enum.GetNames<DatabaseProvider>()));
            return 2;
        }

        try
        {
            // Everything else on the command line is configuration, as it is for the API.
            var configuration = Host.CreateApplicationBuilder([.. rest]).Configuration;

            // An installed instance keeps its settings in a file of its own; --SettingsFile names it.
            SettingsFile.AddTo(configuration);
            var (targetProvider, targetConnectionString) = AxiaDatabase.FromConfiguration(configuration);

            // A SQLite source is read from a copy, so that the database being moved away from is
            // never opened at all, and can be mounted read-only. See SqliteSnapshot.
            using var snapshot = sourceProvider == DatabaseProvider.Sqlite ? SqliteSnapshot.Take(from) : null;
            var sourceConnectionString = snapshot?.ConnectionString ?? from;

            var source = new DbContextOptionsBuilder<AppDbContext>()
                .UseAxiaDatabase(sourceProvider, sourceConnectionString).Options;
            var target = new DbContextOptionsBuilder<AppDbContext>()
                .UseAxiaDatabase(targetProvider, targetConnectionString).Options;

            Log.Information("Copying the {Source} database into the configured {Target} database.", sourceProvider, targetProvider);

            using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
            var copied = await DatabaseCopier.CopyAsync(source, target, loggerFactory.CreateLogger(Name));

            Log.Information("Copied {Rows} row(s) in {Tables} table(s). Start the API on the {Target} database now.",
                copied.Sum(table => table.Rows), copied.Count, targetProvider);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            // Every refusal says what to do about it; a stack trace would only bury that.
            Log.Error("{Command} refused: {Reason}", Name, ex.Message);
            return 1;
        }
        catch (DbException ex)
        {
            // A file that is not there, a server that does not answer, a password it refuses: the
            // driver's own words name it better than anything added here.
            Log.Error("{Command} could not use a database: {Reason}", Name, ex.Message);
            return 1;
        }
    }

    private static string? Take(List<string> args, string option)
    {
        var at = args.IndexOf(option);
        if (at < 0 || at + 1 >= args.Count)
            return null;

        var value = args[at + 1];
        args.RemoveRange(at, 2);
        return value;
    }
}
