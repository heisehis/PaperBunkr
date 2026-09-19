using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeSystemExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentOverrideHex",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDarkThemeKey",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLightThemeKey",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeAutoMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Off");

            migrationBuilder.AddColumn<int>(
                name: "ThemeScheduledDarkHour",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "ThemeScheduledLightHour",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "TrueBlackAutoHour",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrueBlackDark",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
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
