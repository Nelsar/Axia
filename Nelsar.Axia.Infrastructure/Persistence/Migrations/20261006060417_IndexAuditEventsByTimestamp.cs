using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Lets the audit page read newest first without sorting the whole table.
    ///
    /// The column itself is unchanged: Timestamp was already TEXT, and the converter that now
    /// maps it writes the same text SQLite held before (see AuditEventConfiguration). That is
    /// also why the snapshot's property type changes to string with no AlterColumn here.
    /// </summary>
    public partial class IndexAuditEventsByTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents");
        }
    }
}
