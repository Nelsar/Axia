using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Migrations.Sqlite.Migrations
{
    /// <summary>
    /// Records how each directory's connection is protected.
    ///
    /// Every row that exists already gets None, because that is what it has always been: until
    /// now every connection was plain LDAP. Giving them the new default instead would send TLS to
    /// servers listening for plain LDAP on 389, and every directory in the table would stop
    /// answering on the first start after the upgrade. They keep working as before, and each can
    /// be moved to Ldaps or StartTls when its server is ready for it. New rows get Ldaps from the
    /// domain, never from this default.
    /// </summary>
    public partial class AddProviderSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Security",
                table: "Providers",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Security",
                table: "Providers");
        }
    }
}
