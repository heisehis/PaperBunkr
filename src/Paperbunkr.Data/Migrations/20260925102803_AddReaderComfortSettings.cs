using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderComfortSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BreakNudgeIntervalMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<bool>(
                name: "BreakNudgesEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowSessionHud",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WarmShiftEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WarmShiftEndMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 420);

            migrationBuilder.AddColumn<int>(
                name: "WarmShiftStartMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1260);

            migrationBuilder.AddColumn<int>(
                name: "WarmShiftStrength",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 40);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md): DropColumn rebuilds AppSettings and the rollback chain has orphaned columns before; an unused column left behind is harmless.
        }
    }
}
