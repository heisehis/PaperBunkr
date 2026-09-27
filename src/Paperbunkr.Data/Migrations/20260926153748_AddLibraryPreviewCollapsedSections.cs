using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryPreviewCollapsedSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LibraryPreviewCollapsedSections",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "story,file,details");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // AppSettings.LibraryPreviewCollapsedSections is left in place as an orphan on down-migrate, not dropped: a change to AppSettings
            // triggers SQLite's full-table rebuild from the previous model snapshot, which still
            // expects the unmapped LibraryGroupField/LibrarySortField columns and fails with
            // "no such column: LibraryGroupField" (same rule and reason as
            // 20260905064447_AddIssueDuplicateAcknowledged).
        }
    }
}
