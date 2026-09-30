using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Insights History tab (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §1): name
    /// snapshots + the hide-from-History flag on the reading log. Down() really drops the three columns -
    /// unlike the AppSettings/Issues no-op Downs, ReadingEvents carries no unmapped orphan columns, so
    /// SQLite's table rebuild loses nothing.
    /// </summary>
    public partial class AddReadingHistoryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HiddenFromHistory",
                table: "ReadingEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ItemLabel",
                table: "ReadingEvents",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesTitle",
                table: "ReadingEvents",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            // Name snapshots for rows written before this migration, from items still in the library
            // (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §1).
            ReadingHistoryBackfill.Run(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenFromHistory",
                table: "ReadingEvents");

            migrationBuilder.DropColumn(
                name: "ItemLabel",
                table: "ReadingEvents");

            migrationBuilder.DropColumn(
                name: "SeriesTitle",
                table: "ReadingEvents");
        }
    }
}
