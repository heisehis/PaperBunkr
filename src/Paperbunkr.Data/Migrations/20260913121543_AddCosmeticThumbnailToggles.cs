using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCosmeticThumbnailToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DogEarThumbnails",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExportedListsContainFilenames",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FadeInThumbnails",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NumericRatingThumbnails",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowToolTips",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Changed from a real DropColumn to a no-op 2026-09-17: this migration originally matched
            // AddIssueAlternateCount's "brand-new column, real Down()" precedent, but AppSettings is a
            // different table than Issues - a DropColumn here triggers SQLite's full-table-rebuild
            // path, which rebuilds AppSettings from the *previous* migration's model snapshot. Every
            // AppSettings snapshot since UnifyLibrarySortGroupFields (2026-09-03) has LibraryGroupField/
            // LibrarySortField/LibrarySortDirection unmapped (orphaned, still physically present), so
            // that rebuild silently drops them for real. Once dropped, any earlier Down() step whose
            // own rebuild target is a pre-Unify snapshot (which still lists LibraryGroupField as
            // mapped) fails with "no such column: LibraryGroupField" - hit in practice by every
            // migration test rolling back further than this migration, once enough later migrations
            // (this one plus AddConfirmBeforeClose) piled up real drops between them and the Unify
            // boundary. Left in place as orphans on down-migrate instead, the same established pattern
            // as AddNavRailHoverExpandEnabled/AddMetadataWriteBackSettings/etc.
        }
    }
}
