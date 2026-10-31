using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBooksBrowseViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BooksActiveStatusTab",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "All");

            migrationBuilder.AddColumn<string>(
                name: "BooksViewMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Grid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BooksActiveStatusTab",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BooksViewMode",
                table: "AppSettings");
        }
    }
}
