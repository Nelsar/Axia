using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDefaultUserSearchFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserSearchFilter",
                table: "Providers",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 512,
                oldDefaultValue: "(&(objectClass=person)(sAMAccountName={0}))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserSearchFilter",
                table: "Providers",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "(&(objectClass=person)(sAMAccountName={0}))",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 512,
                oldDefaultValue: "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))");
        }
    }
}
