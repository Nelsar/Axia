using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserLookups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLookups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserLookups_Email",
                table: "UserLookups",
                column: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserLookups");
        }
    }
}
