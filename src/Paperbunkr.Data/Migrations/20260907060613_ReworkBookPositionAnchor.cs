using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Replaces <c>Book</c>'s resume-position and <c>BookBookmark</c>'s anchor columns
    /// (docs/superpowers/specs/2026-09-07-books-reader-pagination-and-position-fix-design.md): the old
    /// <c>LastCharacterOffset</c>/<c>CharacterOffset</c> pair addressed a position in the pre-WebView-
    /// redesign "flattened plain-text paragraph stream" - already meaningless since
    /// <c>20260902162546_ReworkBookHighlightAnchor</c> moved highlighting onto the new
    /// <c>BlockId</c>-addressed DOM-element scheme, but never converted for resume-position/bookmarks
    /// at the time. This migration finishes that: <c>BlockId</c>/<c>ProgressionFraction</c> replace the
    /// old offset columns, matching <c>BookHighlight</c>'s own anchor shape. Per the same precedent
    /// that migration set, existing <c>BookBookmark</c> rows are deleted outright (not renamed-in-place
    /// pretending to convert them, which would silently leave rows with an empty, unresolvable
    /// <c>BlockId</c> instead of an honest empty table) - acceptable given negligible userbase, same
    /// reasoning as the highlight migration. <c>Book.LastChapterIndex</c> is untouched (chapter
    /// identity is still a valid concept going forward); only its stored value resets to 0 alongside
    /// the offset-column replacement, since a resume-position without a matching <c>LastBlockId</c> is
    /// no more useful than resetting to the chapter start outright.
    /// </summary>
    public partial class ReworkBookPositionAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM BookBookmarks;");
            migrationBuilder.Sql("UPDATE Books SET LastChapterIndex = 0;");

            migrationBuilder.DropColumn(
                name: "LastCharacterOffset",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CharacterOffset",
                table: "BookBookmarks");

            migrationBuilder.AddColumn<string>(
                name: "LastBlockId",
                table: "Books",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LastProgressionFraction",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockId",
                table: "BookBookmarks",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "ProgressionFraction",
                table: "BookBookmarks",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM BookBookmarks;");
            migrationBuilder.Sql("UPDATE Books SET LastChapterIndex = 0;");

            migrationBuilder.DropColumn(
                name: "LastBlockId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "LastProgressionFraction",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "BlockId",
                table: "BookBookmarks");

            migrationBuilder.DropColumn(
                name: "ProgressionFraction",
                table: "BookBookmarks");

            migrationBuilder.AddColumn<int>(
                name: "LastCharacterOffset",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CharacterOffset",
                table: "BookBookmarks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
