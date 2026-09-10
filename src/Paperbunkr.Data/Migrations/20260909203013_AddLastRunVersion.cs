using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastRunVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastRunVersion",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - LastRunVersion is left in place as an orphan on down-migrate
            // rather than dropped, the same pattern (and for the same reason) as
            // 20260905042026_AddNavRailHoverExpandEnabled and the migrations its own comment cites:
            // a DropColumn here triggers SQLite's full-table-rebuild path, which rebuilds AppSettings
            // from the previous migration's model snapshot - a snapshot that no longer lists the
            // LibraryGroupField/LibrarySortField/LibrarySortDirection columns UnifyLibrarySortGroupFields
            // unmapped without physically dropping. The rebuild would silently drop those, and an
            // earlier Down() that still expects them would then fail with "no such column".
        }
    }
}
