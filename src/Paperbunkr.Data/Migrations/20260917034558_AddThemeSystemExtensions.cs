using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeSystemExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentOverrideHex",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDarkThemeKey",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLightThemeKey",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeAutoMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Off");

            migrationBuilder.AddColumn<int>(
                name: "ThemeScheduledDarkHour",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "ThemeScheduledLightHour",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "TrueBlackAutoHour",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrueBlackDark",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccentOverrideHex",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LastDarkThemeKey",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LastLightThemeKey",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ThemeAutoMode",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ThemeScheduledDarkHour",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ThemeScheduledLightHour",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "TrueBlackAutoHour",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "TrueBlackDark",
                table: "AppSettings");
        }
    }
}
