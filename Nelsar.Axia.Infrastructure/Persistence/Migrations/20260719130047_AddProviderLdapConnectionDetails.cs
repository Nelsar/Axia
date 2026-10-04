using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderLdapConnectionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminUsername",
                table: "Providers",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BaseDn",
                table: "Providers",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedAdminPassword",
                table: "Providers",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FailoverHosts",
                table: "Providers",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserSearchFilter",
                table: "Providers",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "(&(objectClass=person)(sAMAccountName={0}))");

            migrationBuilder.AddColumn<int>(
                name: "UserSearchPageSize",
                table: "Providers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminUsername",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "BaseDn",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "EncryptedAdminPassword",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "FailoverHosts",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "UserSearchFilter",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "UserSearchPageSize",
                table: "Providers");
        }
    }
}
