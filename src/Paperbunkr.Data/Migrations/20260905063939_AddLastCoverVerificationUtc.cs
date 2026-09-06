using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastCoverVerificationUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCoverVerificationUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - LastCoverVerificationUtc is left in place as an orphan on
            // down-migrate rather than dropped, the same pattern (and for the same reason) as
            // 20260905042026_AddNavRailHoverExpandEnabled, 20260904045621_AddBehaviorSettingsBatch2
            // and 20260903211057_AddMetadataWriteBackSettings. A DropColumn here would trigger
            // SQLite's full-table-rebuild path, which rebuilds AppSettings from the previous
            // migration's model snapshot - a snapshot that, because UnifyLibrarySortGroupFields
            // unmapped LibraryGroupField/LibrarySortField/LibrarySortDirection from the entity
            // without physically dropping them, no longer lists those three orphaned columns. The
            // rebuild silently drops them as a side effect, and any earlier Down() step in a
            // rollback chain that still expects them (e.g. 20260827093006_LibraryDetailsColumns)
            // then fails with "no such column: LibraryGroupField".
        }
    }
}
