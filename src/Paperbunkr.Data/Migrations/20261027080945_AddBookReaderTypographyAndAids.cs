using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookReaderTypographyAndAids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HyphenateOverride",
                table: "Books",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JustifyOverride",
                table: "Books",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookReaderAutoScrollSpeed",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "BookReaderCustomFont",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BookReaderHyphenate",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BookReaderJustify",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BookReaderRulerEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BookReaderUsePublisherStyles",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HyphenateOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "JustifyOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "BookReaderAutoScrollSpeed",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderCustomFont",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderHyphenate",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderJustify",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderRulerEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderUsePublisherStyles",
                table: "AppSettings");
        }
    }
}
