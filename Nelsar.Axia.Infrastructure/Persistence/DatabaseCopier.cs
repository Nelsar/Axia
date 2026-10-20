using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Nelsar.Axia.Infrastructure.Persistence;

/// <summary>
/// Copies everything the application keeps — directories and their encrypted bind passwords,
/// role mappings, role permissions, rate limits, the audit trail — from one database into another,
/// whichever two engines they are. How a deployment moves from SQLite to PostgreSQL, or anywhere
/// else <see cref="DatabaseProvider"/> reaches.
///
/// What it insists on, each for a reason:
/// <list type="bullet">
/// <item>The source is at this version's schema. Copying from an older one would mean guessing at
/// columns this version expects; the version being copied from brings it up to date by starting
/// on it once.</item>
/// <item>The target is empty. It is created here, by its own migrations; rows already in it are
/// either a previous copy or a deployment that started on it, and merging into either would
/// leave two of what should be one — two seeds of the same role permissions, say.</item>
/// <item>All of it or none. One transaction on the target, committed after every table's count
/// has been checked against the source's.</item>
/// </list>
///
/// The tables are found in the model, not listed here, so a table added later is copied without
/// anyone remembering to add it — the copy tests fill every table the model has.
/// </summary>
public static class DatabaseCopier
{
    private const int BatchSize = 500;

    public static async Task<IReadOnlyList<(string Table, int Rows)>> CopyAsync(
        DbContextOptions<AppDbContext> from,
        DbContextOptions<AppDbContext> to,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await using var source = new AppDbContext(from);
        await using var target = new AppDbContext(to);

        var applied = (await source.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        if (applied.Count == 0)
        {
            throw new InvalidOperationException(
                "The source holds no Nelsar.Axia database: none of its migrations have been applied there. "
                + "Check the --from connection string.");
        }

        var pending = (await source.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The source database is older than this version ({pending.Count} migration(s) behind, starting with "
                + $"{pending[0]}). Start this version on it once, with its own Database:Provider, which brings it up to "
                + "date, and copy then.");
        }

        await target.Database.MigrateAsync(cancellationToken);

        var tables = InDependencyOrder(target.Model);
        foreach (var table in tables)
        {
            if (await Invoke<bool>(AnyRowsMethod, table, target, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"The target database already has rows in {table.GetTableName()}. Copy into an empty database: "
                    + "one this application has not been started on.");
            }
        }

        var copied = new List<(string Table, int Rows)>();
        await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken);

        foreach (var table in tables)
        {
            var name = table.GetTableName() ?? table.ClrType.Name;
            var rows = await Invoke<int>(CopyTableMethod, table, source, target, cancellationToken);
            var expected = await Invoke<int>(CountMethod, table, source, cancellationToken);
            var written = await Invoke<int>(CountMethod, table, target, cancellationToken);

            if (rows != expected || written != expected)
            {
                throw new InvalidOperationException(
                    $"{name}: the source has {expected} row(s), {rows} were copied and the target holds {written}. "
                    + "Nothing has been committed.");
            }

            logger.LogInformation("Copied {Rows} row(s) of {Table}.", rows, name);
            copied.Add((name, rows));
        }

        await transaction.CommitAsync(cancellationToken);
        return copied;
    }

    /// <summary>A table before any table whose rows point at it.</summary>
    internal static IReadOnlyList<IEntityType> InDependencyOrder(IModel model)
    {
        var ordered = new List<IEntityType>();
        var visiting = new HashSet<IEntityType>();

        void Visit(IEntityType entity)
        {
            if (ordered.Contains(entity) || !visiting.Add(entity))
                return;

            foreach (var principal in entity.GetForeignKeys().Select(fk => fk.PrincipalEntityType).Where(p => !ReferenceEquals(p, entity)))
                Visit(principal);

            ordered.Add(entity);
        }

        foreach (var entity in model.GetEntityTypes().Where(e => !e.IsOwned()).OrderBy(e => e.Name, StringComparer.Ordinal))
            Visit(entity);

        return ordered;
    }

    private static readonly MethodInfo CopyTableMethod = Method(nameof(CopyTableAsync));
    private static readonly MethodInfo CountMethod = Method(nameof(CountAsync));
    private static readonly MethodInfo AnyRowsMethod = Method(nameof(AnyRowsAsync));

    private static MethodInfo Method(string name) =>
        typeof(DatabaseCopier).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task<T> Invoke<T>(MethodInfo method, IEntityType table, params object[] arguments) =>
        (Task<T>)method.MakeGenericMethod(table.ClrType).Invoke(null, arguments)!;

    private static async Task<int> CopyTableAsync<TEntity>(AppDbContext source, AppDbContext target, CancellationToken cancellationToken)
        where TEntity : class
    {
        // Paged in key order, so the audit trail — the one table that grows without bound — is
        // never held in memory whole.
        var key = source.Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties.Single().Name;
        var copied = 0;

        while (true)
        {
            var batch = await source.Set<TEntity>().AsNoTracking()
                .OrderBy(row => EF.Property<object>(row, key))
                .Skip(copied)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                return copied;

            target.Set<TEntity>().AddRange(batch);
            await target.SaveChangesAsync(cancellationToken);
            target.ChangeTracker.Clear();
            copied += batch.Count;
        }
    }

    private static Task<int> CountAsync<TEntity>(AppDbContext context, CancellationToken cancellationToken)
        where TEntity : class =>
        context.Set<TEntity>().CountAsync(cancellationToken);

    private static Task<bool> AnyRowsAsync<TEntity>(AppDbContext context, CancellationToken cancellationToken)
        where TEntity : class =>
        context.Set<TEntity>().AnyAsync(cancellationToken);
}
