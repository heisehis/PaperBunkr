using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateLibraryViewModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLibraryPreviewPanelVisible",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LibraryGridCoverFit",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Poster");

            migrationBuilder.AddColumn<double>(
                name: "LibraryPreviewPanelWidth",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 320.0);

            // Remap persisted legacy view modes (docs/superpowers/specs/2026-09-14-library-visual-
            // redesign-design.md §2). Read the old PanoramaGrid/Tiles values into LibraryGridCoverFit
            // before collapsing them into PosterGrid - once collapsed, the distinction is gone from
            // LibraryViewMode itself. PosterGrid's own stored string is deliberately left completely
            // untouched (see LibraryViewMode.cs's doc comment for why renaming that particular member
            // is a trap this migration specifically avoids) - no AlterColumn anywhere in this
            // migration, on purpose.
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryGridCoverFit = 'Panorama' WHERE LibraryViewMode = 'PanoramaGrid';");
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryGridCoverFit = 'Tiles' WHERE LibraryViewMode = 'Tiles';");
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'PosterGrid' WHERE LibraryViewMode IN ('PanoramaGrid', 'Tiles');");
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'DetailsTable' WHERE LibraryViewMode = 'Details';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'PanoramaGrid' WHERE LibraryViewMode = 'PosterGrid' AND LibraryGridCoverFit = 'Panorama';");
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'Tiles' WHERE LibraryViewMode = 'PosterGrid' AND LibraryGridCoverFit = 'Tiles';");
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'Details' WHERE LibraryViewMode = 'DetailsTable';");

            // Deliberately NOT dropping IsLibraryPreviewPanelVisible/LibraryGridCoverFit/
            // LibraryPreviewPanelWidth here - standing rule for this codebase (see
            // AddNavRailHoverExpandEnabled/LibrarySortGroupAxesAndFinalIssueTriState/etc, and
            // project_paperbunkr_migration_rollback_orphan_column_bug): a DropColumn on AppSettings
            // forces SQLite's full-table-rebuild strategy, which silently drops
            // LibraryGroupField/LibrarySortField/LibrarySortDirection - unmapped since
            // UnifyLibrarySortGroupFields (2026-09-03) but still physically present - because the
            // rebuild's target schema comes from this migration's own model snapshot, which never
            // lists those 3 orphans. No-op-ing new columns' own Down() is this codebase's established
            // way of never creating a fresh instance of that bug. Left as harmless orphans on
            // down-migrate.
        }
    }
}
