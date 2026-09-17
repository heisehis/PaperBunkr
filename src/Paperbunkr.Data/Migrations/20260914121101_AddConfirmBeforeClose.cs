using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmBeforeClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ConfirmBeforeClose",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Changed from a real DropColumn to a no-op 2026-09-17: see
            // AddCosmeticThumbnailToggles's Down() for the full explanation - a DropColumn on
            // AppSettings triggers SQLite's full-table-rebuild path, which silently drops the
            // LibraryGroupField/LibrarySortField/LibrarySortDirection orphans (unmapped since
            // UnifyLibrarySortGroupFields, still physically present) and breaks any earlier Down()
            // step whose rebuild target is a pre-Unify snapshot. Left in place as an orphan on
            // down-migrate instead, the same established pattern as AddNavRailHoverExpandEnabled/
            // AddMetadataWriteBackSettings/etc.
        }
    }
}
