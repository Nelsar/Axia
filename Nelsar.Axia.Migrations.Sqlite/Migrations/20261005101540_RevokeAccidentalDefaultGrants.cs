using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Migrations.Sqlite.Migrations
{
    /// <summary>
    /// Takes back the grants the first seed handed to every account in the directory.
    ///
    /// The seed only ever writes into an empty table, so changing it fixes new installations and
    /// nothing else. A database seeded by an earlier version still lets any signed-in employee
    /// manage groups — and, through group membership, make themselves an administrator — and
    /// reset any account's password through the bulk endpoints. This brings such a database to
    /// the matrix DbInitializer.DefaultGrants now describes.
    ///
    /// Deleting by (role, permission) rather than wholesale is what makes it safe to apply to a
    /// table an administrator may have touched: only these pairs go, every Admin grant and every
    /// grant to a role this project did not seed is left alone. No API has ever been able to add
    /// role permissions, so on a database nobody edited by hand these rows can only be the seed's.
    /// </summary>
    public partial class RevokeAccidentalDefaultGrants : Migration
    {
        private static readonly (string Role, string Permission)[] Revoked =
        [
            // Changing the directory's structure is an administrator's alone.
            ("User", "groups.manage"),
            ("User", "orgunits.manage"),
            ("User", "computers.manage"),
            ("UserCreator", "groups.manage"),
            ("UserCreator", "orgunits.manage"),
            ("UserCreator", "computers.manage"),
            ("UserEditor", "groups.manage"),
            ("UserEditor", "orgunits.manage"),
            ("UserEditor", "computers.manage"),
            ("UserDeleter", "groups.manage"),
            ("UserDeleter", "orgunits.manage"),
            ("UserDeleter", "computers.manage"),

            // Bulk work and the generator that feeds it belong to roles that create or edit
            // accounts. UserEditor keeps users.bulk for update rows; UserCreator keeps both.
            ("User", "users.bulk"),
            ("User", "users.generate"),
            ("UserEditor", "users.generate"),
            ("UserDeleter", "users.bulk"),
            ("UserDeleter", "users.generate"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (role, permission) in Revoked)
            {
                migrationBuilder.Sql(
                    $"DELETE FROM \"RolePermissions\" WHERE \"RoleName\" = '{role}' AND \"Permission\" = '{permission}';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the earlier grants, insecure as they were, so that rolling back leaves the
            // database as the earlier version expects it. Skips any pair already present, which
            // keeps the unique (RoleName, Permission) index satisfied.
            foreach (var (role, permission) in Revoked)
            {
                migrationBuilder.Sql(
                    "INSERT INTO \"RolePermissions\" (\"Id\", \"RoleName\", \"Permission\") "
                    + $"SELECT '{Guid.NewGuid().ToString().ToUpperInvariant()}', '{role}', '{permission}' "
                    + "WHERE NOT EXISTS (SELECT 1 FROM \"RolePermissions\" "
                    + $"WHERE \"RoleName\" = '{role}' AND \"Permission\" = '{permission}');");
            }
        }
    }
}
