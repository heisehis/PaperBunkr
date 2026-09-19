using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderChromeHoverMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReaderChromeHoverMode",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
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
