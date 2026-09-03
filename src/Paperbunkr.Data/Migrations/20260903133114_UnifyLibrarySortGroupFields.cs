using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifyLibrarySortGroupFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Library's per-series and per-issue cards now share one sort/group field pool
            // (IssueListSortField / IssueListGroupField, stored in LibraryIssueListSortField etc.).
            // The old series-only LibrarySortField / LibrarySortDirection / LibraryGroupField
            // columns are dropped. Before dropping them, carry a user's series selection over to the
            // shared column - but only when they never touched the per-issue sort (its column is
            // still at the 'Number' sentinel), so we don't clobber a real per-issue choice.
            //
            // Order matters: run these UPDATEs while the source columns still exist and the table
            // is stable, BEFORE any DropColumn triggers SQLite's 12-step table rebuild (a raw UPDATE
            // issued mid-rebuild is unreliable - EF warns about exactly this; see
            // 20260827045244_LibraryPosterGridConsolidation's own note).
            migrationBuilder.Sql(@"
                UPDATE AppSettings
                SET LibraryIssueListSortField = CASE LibrarySortField
                        WHEN 'Name'        THEN 'Series'
                        WHEN 'DateAdded'   THEN 'Added'
                        WHEN 'LastRead'    THEN 'Opened'
                        WHEN 'Size'        THEN 'FileSize'
                        WHEN 'IssueCount'  THEN 'SeriesIssueCount'
                        WHEN 'UnreadCount' THEN 'SeriesUnreadCount'
                        WHEN 'Publisher'   THEN 'Publisher'
                        ELSE LibraryIssueListSortField
                    END,
                    LibraryIssueListSortDirection = LibrarySortDirection
                WHERE LibraryIssueListSortField = 'Number';");

            // Group fields map 1:1 - None / ContentType / Publisher / Alphabetical all now exist in
            // IssueListGroupField too. Only carry over when the shared group column is untouched.
            migrationBuilder.Sql(@"
                UPDATE AppSettings
                SET LibraryIssueListGroupField = LibraryGroupField
                WHERE LibraryIssueListGroupField = 'None'
                  AND LibraryGroupField IN ('ContentType', 'Publisher', 'Alphabetical');");

            // The 3 old columns are left in place as orphans (no longer mapped by AppSettings) rather
            // than physically dropped: a DropColumn here would break an *older* Paperbunkr build - one
            // still mapping LibrarySortField etc. - if it opens this DB afterwards, and this DB is the
            // shared per-user dev DB across worktrees. EF ignores unmapped columns; a later cleanup
            // migration can drop them once no old build is in play.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only Up (the columns were never dropped) - nothing structural to reverse. The
            // carry-over is not undone; the series selection simply lives on in
            // LibraryIssueListSortField, which is harmless there.
        }
    }
}
