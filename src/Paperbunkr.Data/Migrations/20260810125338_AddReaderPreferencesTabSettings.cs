using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderPreferencesTabSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DefaultAutoRotate",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPageFitMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "FitWidth");

            migrationBuilder.AddColumn<double>(
                name: "MouseWheelSpeed",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 2.0);

            migrationBuilder.AddColumn<bool>(
                name: "ResetZoomOnPageChange",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultAutoRotate",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPageFitMode",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MouseWheelSpeed",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ResetZoomOnPageChange",
                table: "AppSettings");
        }
    }
}
