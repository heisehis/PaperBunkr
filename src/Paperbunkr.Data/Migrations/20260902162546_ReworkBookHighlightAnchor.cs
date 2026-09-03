using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Replaces <c>BookHighlight</c>'s anchor columns (docs/superpowers/specs/2026-09-02-books-
    /// reflow-reader-webview-redesign-design.md): the old <c>StartOffset</c>/<c>EndOffset</c> pair
    /// addressed a position in the *flattened plain-text paragraph stream* the pre-redesign reader
    /// rendered; the new <c>BlockId</c>/<c>StartOffset</c>/<c>Length</c> addresses a specific DOM
    /// element in the real HTML the WebView-based reader renders instead - a fundamentally different,
    /// non-convertible position encoding. Per the design's explicit decision, existing highlight rows
    /// are deleted outright (not renamed-in-place pretending to convert them, which would silently
    /// leave rows with a meaningless <c>Length</c> value and an empty, unresolvable
    /// <c>BlockId</c> instead of an honest empty table) - acceptable given negligible userbase at the
    /// time of this migration; a user-facing warning dialog before this ships broadly is a tracked
    /// follow-up, not part of this migration.
    /// </summary>
    public partial class ReworkBookHighlightAnchor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM BookHighlights;");

            migrationBuilder.DropColumn(name: "EndOffset", table: "BookHighlights");

            migrationBuilder.AddColumn<string>(
                name: "BlockId",
                table: "BookHighlights",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Length",
                table: "BookHighlights",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM BookHighlights;");

            migrationBuilder.DropColumn(name: "Length", table: "BookHighlights");
            migrationBuilder.DropColumn(name: "BlockId", table: "BookHighlights");

            migrationBuilder.AddColumn<int>(
                name: "EndOffset",
                table: "BookHighlights",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
