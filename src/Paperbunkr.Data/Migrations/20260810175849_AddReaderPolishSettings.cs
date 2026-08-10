using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderPolishSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BrightnessOverride",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ContrastOverride",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "GammaOverride",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "SaturationOverride",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundColor",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "WhiteSmoke");

            migrationBuilder.AddColumn<double>(
                name: "DefaultBrightness",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultContrast",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultGamma",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultSaturation",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ImageBackgroundMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Color");

            migrationBuilder.AddColumn<double>(
                name: "MagnifierOpacity",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<int>(
                name: "MagnifierSizePixels",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<double>(
                name: "MagnifierZoom",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 2.0);

            migrationBuilder.AddColumn<bool>(
                name: "PageMarginEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "PageMarginPercentWidth",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.050000000000000003);

            migrationBuilder.AddColumn<bool>(
                name: "ShowScrubberOverlay",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrightnessOverride",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ContrastOverride",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "GammaOverride",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "SaturationOverride",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BackgroundColor",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultBrightness",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultContrast",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultGamma",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultSaturation",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ImageBackgroundMode",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MagnifierOpacity",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MagnifierSizePixels",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MagnifierZoom",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PageMarginEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PageMarginPercentWidth",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ShowScrubberOverlay",
                table: "AppSettings");
        }
    }
}
