using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBooksBrowseState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChapterCount",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Finished",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BooksGroupField",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "BooksSortDirection",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Ascending");

            migrationBuilder.AddColumn<string>(
                name: "BooksSortField",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChapterCount",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Finished",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "BooksGroupField",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BooksSortDirection",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BooksSortField",
                table: "AppSettings");
        }
    }
}
