using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookReaderErgonomicsAndAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CharacterSpacingOverride",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FontFamilyOverride",
                table: "Books",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FontSizeOverride",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineSpacingOverride",
                table: "Books",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PageMarginOverride",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ParagraphSpacingOverride",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeOverride",
                table: "Books",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WordSpacingOverride",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BookReaderAutoHideChrome",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<double>(
                name: "BookReaderCharacterSpacing",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "BookReaderFontFamily",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Serif");

            migrationBuilder.AddColumn<double>(
                name: "BookReaderFontSize",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 17.0);

            migrationBuilder.AddColumn<string>(
                name: "BookReaderLineSpacing",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<double>(
                name: "BookReaderPageMargin",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 40.0);

            migrationBuilder.AddColumn<double>(
                name: "BookReaderParagraphSpacing",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 10.0);

            migrationBuilder.AddColumn<string>(
                name: "BookReaderTheme",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "MatchAppSkin");

            migrationBuilder.AddColumn<double>(
                name: "BookReaderWordSpacing",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "BookAnnotationImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    RectX = table.Column<double>(type: "REAL", nullable: false),
                    RectY = table.Column<double>(type: "REAL", nullable: false),
                    RectWidth = table.Column<double>(type: "REAL", nullable: false),
                    RectHeight = table.Column<double>(type: "REAL", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAnnotationImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookAnnotationImages_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookHighlights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChapterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Yellow"),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Excerpt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookHighlights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookHighlights_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookAnnotationImages_BookId",
                table: "BookAnnotationImages",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookHighlights_BookId",
                table: "BookHighlights",
                column: "BookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAnnotationImages");

            migrationBuilder.DropTable(
                name: "BookHighlights");

            migrationBuilder.DropColumn(
                name: "CharacterSpacingOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "FontFamilyOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "FontSizeOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "LineSpacingOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PageMarginOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ParagraphSpacingOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ThemeOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "WordSpacingOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "BookReaderAutoHideChrome",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderCharacterSpacing",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderFontFamily",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderFontSize",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderLineSpacing",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderPageMargin",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderParagraphSpacing",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderTheme",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderWordSpacing",
                table: "AppSettings");
        }
    }
}
