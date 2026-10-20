using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Tests;

/// <summary>
/// What signing in grants by itself, on a fresh database and on one seeded by an earlier version.
///
/// The first seed gave every account in the directory groups.manage, orgunits.manage,
/// computers.manage and users.bulk. With group management, anyone could add themselves to the
/// group mapped to Admin; with users.bulk, anyone could reset any account's password through an
/// "update" row.
/// </summary>
public class DefaultGrantsTests
{
    /// <summary>Permissions that change the directory's structure or this application's settings.</summary>
    private static readonly string[] AdministrativeOnly =
    [
        Permissions.GroupsManage,
        Permissions.OrganizationalUnitsManage,
        Permissions.ComputersManage,
        Permissions.ProvidersManage,
        Permissions.RolesManage,
        Permissions.RateLimitsManage,
    ];

    [Fact]
    public void ASignedInUser_CanOnlyRead()
    {
        Assert.Equal(
            new[] { Permissions.UsersRead, Permissions.GroupsRead, Permissions.OrganizationalUnitsRead, Permissions.ComputersRead }
                .Order(),
            DbInitializer.DefaultGrants[UserRoles.User].Order());
    }

    [Fact]
    public void OnlyAdministrators_ChangeTheDirectoryStructureOrTheSettings()
    {
        foreach (var (role, permissions) in DbInitializer.DefaultGrants)
        {
            if (role == UserRoles.Admin)
                continue;

            Assert.Empty(permissions.Intersect(AdministrativeOnly));
        }
    }

    [Fact]
    public void EveryRoleThatRunsBatches_CanDoWhatABatchRowDoes()
    {
        // users.bulk on its own lets a caller submit batches; each row still needs users.create
        // or users.update. A role holding only users.bulk would be a role whose batches all fail.
        foreach (var (_, permissions) in DbInitializer.DefaultGrants)
        {
            if (permissions.Contains(Permissions.UsersBulk))
                Assert.True(permissions.Contains(Permissions.UsersCreate) || permissions.Contains(Permissions.UsersUpdate));
        }
    }

    [Fact]
    public async Task TheMigration_BringsAnEarlierSeedToTheCurrentDefaults()
    {
        // A database seeded by an earlier version keeps its grants: the seed only writes into an
        // empty table. The migration is what reaches it.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20260802091100_AddRolePermissions");
        foreach (var (role, permissions) in EarlierSeed())
            foreach (var permission in permissions)
                db.RolePermissions.Add(RolePermission.Grant(role, permission));
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();

        var granted = await db.RolePermissions.AsNoTracking().ToListAsync();
        foreach (var (role, expected) in DbInitializer.DefaultGrants)
        {
            var actual = granted.Where(g => g.RoleName == role).Select(g => g.Permission);
            Assert.Equal(expected.Order(), actual.Order());
        }
    }

    [Fact]
    public async Task TheMigration_LeavesGrantsItDidNotSeedAlone()
    {
        // Deleting by (role, permission) is what keeps a deliberate grant — to a role this project
        // never seeded — from being swept away with the accidental ones.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20260802091100_AddRolePermissions");
        db.RolePermissions.Add(RolePermission.Grant("GroupStewards", Permissions.GroupsManage));
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();

        Assert.Contains(await db.RolePermissions.AsNoTracking().ToListAsync(),
            g => g.RoleName == "GroupStewards" && g.Permission == Permissions.GroupsManage);
    }

    /// <summary>The matrix the first seed wrote, reproduced as it was.</summary>
    private static Dictionary<string, string[]> EarlierSeed()
    {
        string[] everyAuthenticatedUser =
        [
            Permissions.UsersRead,
            Permissions.UsersBulk,
            Permissions.UsersGenerate,
            Permissions.GroupsRead,
            Permissions.GroupsManage,
            Permissions.OrganizationalUnitsRead,
            Permissions.OrganizationalUnitsManage,
            Permissions.ComputersRead,
            Permissions.ComputersManage,
        ];

        return new Dictionary<string, string[]>
        {
            [UserRoles.User] = everyAuthenticatedUser,
            [UserRoles.UserCreator] = [.. everyAuthenticatedUser, Permissions.UsersList, Permissions.UsersCreate],
            [UserRoles.UserEditor] = [.. everyAuthenticatedUser, Permissions.UsersUpdate],
            [UserRoles.UserDeleter] = [.. everyAuthenticatedUser, Permissions.UsersDelete],
            [UserRoles.Admin] = [.. Permissions.All],
        };
    }
}
