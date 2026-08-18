using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageTransitionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageTransitionDurationMs",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 250);

            migrationBuilder.AddColumn<string>(
                name: "PageTransitionStyle",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageTransitionDurationMs",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PageTransitionStyle",
                table: "AppSettings");
        }
    }
}
