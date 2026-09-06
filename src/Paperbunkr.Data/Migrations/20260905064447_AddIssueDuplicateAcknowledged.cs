using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueDuplicateAcknowledged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DuplicateAcknowledged",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - DuplicateAcknowledged is left in place as an orphan on
            // down-migrate rather than dropped, the same pattern (and for the same reason) as
            // 20260905042026_AddNavRailHoverExpandEnabled, 20260904045621_AddBehaviorSettingsBatch2
            // and 20260903211057_AddMetadataWriteBackSettings. A DropColumn here (on Issues, but the
            // hazard is identical to the AppSettings case) would trigger SQLite's full-table-rebuild
            // path, which rebuilds from the previous migration's model snapshot. Because
            // UnifyLibrarySortGroupFields unmapped LibraryGroupField/LibrarySortField/
            // LibrarySortDirection from AppSettings without physically dropping them, a rebuild
            // anywhere in the rollback chain can silently drop those orphaned columns, and any
            // earlier Down() step that still expects them (e.g. 20260827093006_LibraryDetailsColumns)
            // then fails with "no such column: LibraryGroupField".
        }
    }
}
