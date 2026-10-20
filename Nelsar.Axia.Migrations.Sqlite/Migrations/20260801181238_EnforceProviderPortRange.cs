using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nelsar.Axia.Migrations.Sqlite.Migrations
{
    /// <summary>
    /// Provider.Port became the LdapPort value object. The column stays INTEGER, so there is no
    /// schema change — but the invariant is now enforced when EF materialises a row, and rows
    /// written while the API accepted any int would start throwing on a plain read.
    /// </summary>
    public partial class EnforceProviderPortRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair rows that predate the invariant. Port was a plain int with [Required],
            // which is a no-op on a non-nullable int, so 0 (the CLR default) and any out-of-range
            // value could be stored. Left alone, such a row would make GET /api/v1/providers fail
            // for the whole collection — one bad row taking down a read.
            //
            // 389 is the IANA default and matches LdapPort.Default; a provider that was
            // unreachable on an impossible port is no worse off pointing at the standard one.
            migrationBuilder.Sql(
                "UPDATE Providers SET Port = 389 WHERE Port < 1 OR Port > 65535;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the original values were unusable and are not recoverable, and
            // restoring them would only reintroduce rows the application cannot read.
        }
    }
}
