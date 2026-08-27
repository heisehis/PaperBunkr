using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibraryPosterGridConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADD COLUMN is a plain ALTER (no table rebuild). Do it, then the data remap, THEN the
            // AlterColumn default change - AlterColumn forces SQLite's 12-step table rebuild, and a
            // raw UPDATE issued while that rebuild is pending is unreliable (EF warns about exactly
            // this). Running the UPDATEs against the still-stable table first sidesteps it.
            migrationBuilder.AddColumn<bool>(
                name: "LibraryShowTileTitles",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Remap persisted legacy view modes (docs/superpowers/specs/2026-08-27-library-
            // browsing-4a-poster-grid-design.md §3). Order matters: read 'CoverOnlyGrid' to set
            // titles-off BEFORE the rename collapses it into 'PosterGrid'.
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryShowTileTitles = 0 WHERE LibraryViewMode = 'CoverOnlyGrid';");
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'PosterGrid' WHERE LibraryViewMode IN ('CompactGrid', 'ComfortableGrid', 'CoverOnlyGrid');");

            migrationBuilder.AlterColumn<string>(
                name: "LibraryViewMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "PosterGrid",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32,
                oldDefaultValue: "ComfortableGrid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse - the Compact/CoverOnly distinction is unrecoverable, everything
            // that was collapsed lands back on ComfortableGrid.
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'ComfortableGrid' WHERE LibraryViewMode = 'PosterGrid';");

            migrationBuilder.DropColumn(
                name: "LibraryShowTileTitles",
                table: "AppSettings");

            migrationBuilder.AlterColumn<string>(
                name: "LibraryViewMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "ComfortableGrid",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32,
                oldDefaultValue: "PosterGrid");
        }
    }
}
