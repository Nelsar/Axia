using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddLdapConfigurationStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LdapConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    BaseDn = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BindDn = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EncryptedBindPassword = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    UserObjectClass = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LdapConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LdapAttributeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LdapConfigurationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DomainPropertyName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LdapAttributeName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LdapAttributeMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LdapAttributeMappings_LdapConfigurations_LdapConfigurationId",
                        column: x => x.LdapConfigurationId,
                        principalTable: "LdapConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LdapAttributeMappings_LdapConfigurationId_DomainPropertyName",
                table: "LdapAttributeMappings",
                columns: new[] { "LdapConfigurationId", "DomainPropertyName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LdapAttributeMappings");

            migrationBuilder.DropTable(
                name: "LdapConfigurations");
        }
    }
}
