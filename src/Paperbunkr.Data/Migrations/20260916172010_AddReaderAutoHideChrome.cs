using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderAutoHideChrome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's own scaffold defaulted this to false - wrong: the C# property default is true
            // (matching the hardcoded-always-on behavior this setting replaces), and a brand-new row
            // via GetOrCreateAppSettings() picks that up correctly, but an EXISTING user's AppSettings
            // row upgrading through this migration would otherwise silently get auto-hide OFF instead
            // of the "on" they already effectively had - a real, easy-to-miss default-value mismatch.
            migrationBuilder.AddColumn<bool>(
                name: "ReaderAutoHideChrome",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReaderAutoHideChrome",
                table: "AppSettings");
        }
    }
}
