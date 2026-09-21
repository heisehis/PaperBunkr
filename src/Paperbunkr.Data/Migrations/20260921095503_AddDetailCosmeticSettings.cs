using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailCosmeticSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HeroBackdrop",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeriesAccentColor",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op, same established AppSettings pattern as AddCosmeticsPitchSettings / AddShowSelectionCheckbox: a real
            // DropColumn on AppSettings triggers SQLite's full-table rebuild, which silently drops the orphaned
            // LibraryGroupField/LibrarySortField/LibrarySortDirection columns. The columns persist as harmless orphans.
        }
    }
}
