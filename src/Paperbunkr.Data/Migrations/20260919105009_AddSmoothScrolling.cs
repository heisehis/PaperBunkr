using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Adds <c>AppSettings.SmoothScrolling</c> (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md 6). Additive only:
    /// one boolean column defaulting to true, so a build that does not know it keeps working against this database.
    /// Hand-written on purpose: the committed model snapshot at this point already contained tracker columns the entity model
    /// does not (unfinished work elsewhere), so a scaffolded migration would have dropped them.
    /// </summary>
    public partial class AddSmoothScrolling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SmoothScrolling",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - the column is left in place as an orphan on down-migrate.
            // EF's SQLite DropColumn rebuilds the whole AppSettings table from the previous migration's model
            // snapshot, which silently drops the orphaned LibraryGroupField/LibrarySortField/
            // LibrarySortDirection columns and breaks any earlier Down() step in the same rollback
            // ("no such column: LibraryGroupField"). Same convention as AddMatrixRainEnabled /
            // AddCosmeticThumbnailToggles / AddTrackerBehaviorSettings.
        }
    }
}
