using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookMetadataFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately NOT dropping Books.Finished: Book.Finished is [NotMapped] now (a view over Status), but the legacy
            // column stays in the table, unused, so no DropColumn rollback chain exists to go wrong (see memory
            // migration_rollback_orphan_column_bug). The model snapshot no longer lists it; any later table rebuild may drop it harmlessly.
            migrationBuilder.AddColumn<float>(
                name: "CommunityRating",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommunityRatingCount",
                table: "Books",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ISBN",
                table: "Books",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LanguageISO",
                table: "Books",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Publisher",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "Rating",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "SeriesIndex",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipMetadataMatch",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Books",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unread");

            migrationBuilder.AddColumn<string>(
                name: "Subtitle",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BookMetadataBackfillDone",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BookMetadataGoogleBooksEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BookMetadataOpenLibraryEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Books.Status data step: Finished rows stay Finished; a book with any reading position is Reading; the rest are Unread.
            migrationBuilder.Sql(
                "UPDATE Books SET Status = CASE " +
                "WHEN Finished = 1 THEN 'Finished' " +
                "WHEN LastBlockId IS NOT NULL AND LastBlockId <> '' THEN 'Reading' " +
                "WHEN LastProgressionFraction IS NOT NULL AND LastProgressionFraction > 0 THEN 'Reading' " +
                "WHEN LastChapterIndex > 0 THEN 'Reading' " +
                "ELSE 'Unread' END;");

            migrationBuilder.CreateTable(
                name: "BookFieldSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceRef = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookFieldSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookFieldSources_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Category = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookTags_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookFieldSources_BookId_Field",
                table: "BookFieldSources",
                columns: new[] { "BookId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookTags_BookId_Field_Value",
                table: "BookTags",
                columns: new[] { "BookId", "Field", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookTags_Value",
                table: "BookTags",
                column: "Value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookFieldSources");

            migrationBuilder.DropTable(
                name: "BookTags");

            migrationBuilder.DropColumn(
                name: "CommunityRating",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CommunityRatingCount",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ISBN",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "LanguageISO",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Publisher",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SeriesIndex",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SkipMetadataMatch",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Subtitle",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "BookMetadataBackfillDone",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookMetadataGoogleBooksEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookMetadataOpenLibraryEnabled",
                table: "AppSettings");
        }
    }
}
