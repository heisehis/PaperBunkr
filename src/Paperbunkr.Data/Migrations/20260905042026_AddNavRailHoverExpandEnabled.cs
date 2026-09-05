using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNavRailHoverExpandEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NavRailHoverExpandEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - left in place as an orphan on down-migrate rather than dropped,
            // the same pattern (and for the same reason) as 20260903211057_AddMetadataWriteBackSettings
            // and 20260904045621_AddBehaviorSettingsBatch2. A DropColumn here would trigger SQLite's
            // full-table-rebuild path, which rebuilds AppSettings from the *previous* migration's
            // model snapshot (AddBehaviorSettingsBatch2's) - a snapshot that, because
            // UnifyLibrarySortGroupFields unmapped LibraryGroupField/LibrarySortField/
            // LibrarySortDirection from the entity without physically dropping them, no longer lists
            // those three orphaned columns. The rebuild would silently drop them as a side effect,
            // and any earlier Down() step that still expects them (e.g.
            // 20260827093006_LibraryDetailsColumns, whose own rebuild target snapshot predates the
            // unify and still lists LibraryGroupField) would then fail with "no such column:
            // LibraryGroupField" - confirmed the actual cause of that failure via
            // `dotnet ef migrations script`.
        }
    }
}
