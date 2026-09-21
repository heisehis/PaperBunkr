using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShowSelectionCheckbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowSelectionCheckbox",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op, same established AppSettings pattern as AddCosmeticThumbnailToggles / AddCosmeticsPitchSettings:
            // a real DropColumn on AppSettings triggers SQLite's full-table rebuild, which silently drops the
            // orphaned LibraryGroupField/LibrarySortField/LibrarySortDirection columns. The column persists as a
            // harmless orphan on down-migrate.
        }
    }
}
