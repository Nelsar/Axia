using Microsoft.Data.Sqlite;

namespace Nelsar.Axia.Infrastructure.Persistence;

/// <summary>
/// A copy of a SQLite database in a folder of its own, to be read instead of the original, and
/// removed afterwards. How copy-database reads a SQLite source.
///
/// The source used to be opened in place, read-only. That failed for the database it is meant for:
/// SQLite runs this application's databases in WAL mode, and a clean shutdown removes the -wal and
/// -shm files, which a reader then has to create beside the database — impossible on the read-only
/// mount the steps in docs/operations.md use, so the copy stopped at "unable to open database
/// file". A copy is opened like any database, never touches the original, and still carries what a
/// shutdown that was not clean left in the -wal file. The -shm file is an index SQLite rebuilds from
/// the -wal, and is left behind.
/// </summary>
public sealed class SqliteSnapshot : IDisposable
{
    private readonly string _folder;

    private SqliteSnapshot(string folder, string connectionString)
    {
        _folder = folder;
        ConnectionString = connectionString;
    }

    /// <summary>A connection string for the copy.</summary>
    public string ConnectionString { get; }

    /// <summary>Copies the database a connection string names.</summary>
    /// <exception cref="InvalidOperationException">There is no file there: a mistyped path, or one
    /// outside the container it runs in. Opening it would have made a new, empty database.</exception>
    public static SqliteSnapshot Take(string connectionString)
    {
        var source = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new InvalidOperationException(
                $"There is no SQLite database at '{source}'. Check the --from connection string; in a container, the "
                + "path is the one inside it, where the old database's volume is mounted.");
        }

        var folder = Directory.CreateTempSubdirectory("axia-copy-").FullName;
        var copy = Path.Combine(folder, Path.GetFileName(source));

        File.Copy(source, copy);
        if (File.Exists(source + "-wal"))
            File.Copy(source + "-wal", copy + "-wal");

        return new SqliteSnapshot(folder, new SqliteConnectionStringBuilder { DataSource = copy }.ToString());
    }

    public void Dispose()
    {
        // Pooled connections keep the file open, and an open file cannot be deleted on Windows.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not worth failing a copy that has succeeded.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
