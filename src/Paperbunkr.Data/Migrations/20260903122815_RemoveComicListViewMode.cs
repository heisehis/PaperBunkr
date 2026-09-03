using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveComicListViewMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The "Comic List" (LibraryViewMode.IssueList) display mode was removed 2026-09-03 - it
            // was a redundant flat per-issue list that Details already covers. LibraryViewMode is
            // stored by string name (HasConversion<string>), so a persisted 'IssueList' would fail
            // to parse on load; remap it to the closest survivor. Pure data UPDATE, no schema
            // change - same shape as 20260827045244_LibraryPosterGridConsolidation's legacy remap.
            migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'Details' WHERE LibraryViewMode = 'IssueList';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: which rows were 'IssueList' before the Up remap is unrecoverable, and
            // the enum member no longer exists to remap back to. Down is intentionally a no-op.
        }
    }
}
