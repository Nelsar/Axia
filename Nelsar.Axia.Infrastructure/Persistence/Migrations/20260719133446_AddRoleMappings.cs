using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RoleName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleMappings_ProviderId_GroupName",
                table: "RoleMappings",
                columns: new[] { "ProviderId", "GroupName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleMappings");
        }
    }
}
