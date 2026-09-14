using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCosmeticThumbnailToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DogEarThumbnails",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExportedListsContainFilenames",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FadeInThumbnails",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NumericRatingThumbnails",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowToolTips",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DogEarThumbnails",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ExportedListsContainFilenames",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FadeInThumbnails",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "NumericRatingThumbnails",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ShowToolTips",
                table: "AppSettings");
        }
    }
}
