using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Migrations.Sqlite;

/// <summary>
/// The context <c>dotnet ef</c> builds migrations for this project from:
///
/// <code>
/// dotnet ef migrations add Name --project Nelsar.Axia.Migrations.Sqlite --startup-project Nelsar.Axia.Api
/// </code>
///
/// Adding a migration connects to nothing, so the connection string only has to parse.
/// </summary>
public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseAxiaDatabase(DatabaseProvider.Sqlite, AxiaDatabase.SqliteDefaultConnectionString)
            .Options);
}
