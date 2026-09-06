using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingEventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharacterCount",
                table: "Books",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadingEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PagesRead = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PrimaryGenre = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingEvents_ItemType_ItemId",
                table: "ReadingEvents",
                columns: new[] { "ItemType", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingEvents_TimestampUtc",
                table: "ReadingEvents",
                column: "TimestampUtc");

            // One-time best-effort backfill from the point-in-time read state that predates this log
            // (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md §4.1). Pre-existing
            // history collapses onto single timestamps - accepted; self-corrects as real events
            // accrue. SQL lives in ReadingEventBackfill so it can be unit-tested directly.
            ReadingEventBackfill.Run(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingEvents");

            // Books.CharacterCount is deliberately NOT dropped on down-migrate - left as an orphan
            // column, the same pattern (and for the same reason) as AddNavRailHoverExpandEnabled /
            // AddBehaviorSettingsBatch2 / AddMetadataWriteBackSettings. A DropColumn here triggers
            // SQLite's full-table-rebuild path, which recreates the table from a model snapshot that
            // no longer lists columns other migrations orphaned but never physically dropped - the
            // rebuild silently drops them and a later Down() step in a rollback chain then fails
            // with "no such column". A nullable orphan column costs nothing.
        }
    }
}
