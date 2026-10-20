using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRateLimitSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateLimitSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PolicyName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PermitLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    WindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitSettings_PolicyName",
                table: "RateLimitSettings",
                column: "PolicyName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RateLimitSettings");
        }
    }
}
