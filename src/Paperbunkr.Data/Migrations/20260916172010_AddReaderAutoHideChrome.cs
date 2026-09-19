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
            // Deliberately a no-op (2026-09-19) - columns are left in place as orphans on down-migrate.
            // EF's SQLite DropColumn rebuilds the whole AppSettings table from the previous migration's model
            // snapshot, which silently drops the orphaned LibraryGroupField/LibrarySortField/
            // LibrarySortDirection columns and breaks any earlier Down() step in the same rollback
            // ("no such column: LibraryGroupField"). Same convention as AddCosmeticThumbnailToggles /
            // AddTrackerBehaviorSettings / AddNavRailHoverExpandEnabled.
        }
    }
}
