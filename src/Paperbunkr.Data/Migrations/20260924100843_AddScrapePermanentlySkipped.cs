using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapePermanentlySkipped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ScrapePermanentlySkipped",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Issues.ScrapePermanentlySkipped is left in place as an orphan on down-migrate, not dropped: a change to Issues
            // triggers SQLite's full-table rebuild from the previous model snapshot, which still
            // expects the unmapped LibraryGroupField/LibrarySortField columns and fails with
            // "no such column: LibraryGroupField" (same rule and reason as
            // 20260905064447_AddIssueDuplicateAcknowledged).
        }
    }
}
