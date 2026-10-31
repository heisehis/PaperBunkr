using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookReaderLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookPageWordsMax",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<int>(
                name: "BookPageWordsMin",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 250);

            migrationBuilder.AddColumn<string>(
                name: "BookReaderLayout",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Paged");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookPageWordsMax",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookPageWordsMin",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BookReaderLayout",
                table: "AppSettings");
        }
    }
}
