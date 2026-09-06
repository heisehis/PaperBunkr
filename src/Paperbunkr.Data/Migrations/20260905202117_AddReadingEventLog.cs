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

            // Unlike AppSettings/Issues (AddNavRailHoverExpandEnabled, AddBehaviorSettingsBatch2,
            // AddMetadataWriteBackSettings, AddIssueDuplicateAcknowledged), Books has no pre-existing
            // unmapped-but-undropped orphan column for a rebuild to collaterally wipe - CharacterCount
            // is still a live, mapped Book property, and nothing else on Books has ever been silently
            // unmapped. The no-op-Down workaround those migrations need doesn't apply here, and
            // applying it anyway broke a genuine down-then-up round trip: leaving CharacterCount in
            // place meant the next forward Migrate() re-ran this Up() and failed with "duplicate
            // column name: CharacterCount" (caught by ReworkBookHighlightAnchorMigrationTests, which
            // rolls back below this migration and then migrates forward again). A real DropColumn is
            // safe and correct here.
            migrationBuilder.DropColumn(
                name: "CharacterCount",
                table: "Books");
        }
    }
}
