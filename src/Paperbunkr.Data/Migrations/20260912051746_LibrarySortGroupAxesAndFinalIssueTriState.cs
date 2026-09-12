using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibrarySortGroupAxesAndFinalIssueTriState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsFinalIssue",
                table: "Issues",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "LibraryGroupVirtualTagId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibrarySortVirtualTagId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op. Two reasons, stronger together than either alone:
            // (1) Same pattern as AddReaderMemoryLimitMb/AddNavRailHoverExpandEnabled/
            // AddBehaviorSettingsBatch2/AddMetadataWriteBackSettings/
            // AddReaderBackgroundTextureAndSpreadPosition for the two AddColumn calls
            // (LibrarySortVirtualTagId/LibraryGroupVirtualTagId): a DropColumn triggers SQLite's
            // full-table-rebuild, which rebuilds from an earlier model snapshot that no longer lists
            // other columns unmapped-but-not-physically-dropped by later migrations, silently
            // dropping them (memory: project_paperbunkr_migration_rollback_orphan_column_bug). Both
            // columns are left as harmless orphans on down-migrate.
            // (2) The IsFinalIssue AlterColumn is a genuinely different, higher-risk case than a
            // pure addition: by the time this would ever run, real rows exist with IsFinalIssue =
            // NULL (that's the entire point of the tri-state - new/never-touched issues default to
            // it). SQLite's rebuild-based AlterColumn narrowing nullable -> NOT NULL is not verified
            // here to safely coalesce those existing NULLs to `false` during the copy step - reverting
            // could either throw a NOT NULL constraint violation or (per the up-down-up antipattern
            // this project already avoids - project_paperbunkr_migration_updown_up_test_antipattern)
            // paper over the wrong behavior in a same-session round-trip without ever exercising the
            // failure mode a real deployed database would hit. Leaving IsFinalIssue nullable on
            // down-migrate is the safe direction, not a partial revert.
        }
    }
}
