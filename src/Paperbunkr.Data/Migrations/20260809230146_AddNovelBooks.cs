using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNovelBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortName = table.Column<string>(type: "TEXT", nullable: true),
                    Author = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    CoverImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    PublishedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AddedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastOpenedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCharacterOffset = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Books_BookSeries_BookSeriesId",
                        column: x => x.BookSeriesId,
                        principalTable: "BookSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BookBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Excerpt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookBookmarks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookBookmarks_BookId",
                table: "BookBookmarks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookFolders_Path",
                table: "BookFolders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_BookSeriesId",
                table: "Books",
                column: "BookSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_FilePath",
                table: "Books",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_BookSeries_Name",
                table: "BookSeries",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookBookmarks");

            migrationBuilder.DropTable(
                name: "BookFolders");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "BookSeries");
        }
    }
}
