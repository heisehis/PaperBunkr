using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderInputSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContinuousTapZoneInvert",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "ContinuousTapZoneLayout",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Disabled");

            migrationBuilder.AddColumn<bool>(
                name: "ExtraMouseButtonsTurnPages",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "GamepadEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PagedTapZoneInvert",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "PagedTapZoneLayout",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.AddColumn<bool>(
                name: "TapZonesForMouse",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md): DropColumn rebuilds AppSettings and the rollback chain has orphaned columns before; an unused column left behind is harmless.
        }
    }
}
