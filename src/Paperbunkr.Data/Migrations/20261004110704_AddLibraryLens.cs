using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryLens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LibraryLens",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "All");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op, as in AddReadingListViewMode: DropColumn rebuilds AppSettings, and the rollback chain has orphaned columns before; an unused column left behind is harmless.
        }
    }
}
