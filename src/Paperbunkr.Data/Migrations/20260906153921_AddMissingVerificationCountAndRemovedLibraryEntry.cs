using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingVerificationCountAndRemovedLibraryEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MissingVerificationCount",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLibraryHealthVerifyUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemovedLibraryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesName = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    Number = table.Column<string>(type: "TEXT", nullable: true),
                    Volume = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemovedLibraryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemovedLibraryEntries_RemovedAtUtc",
                table: "RemovedLibraryEntries",
                column: "RemovedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // RemovedLibraryEntries is a brand-new table, safe to drop outright.
            migrationBuilder.DropTable(
                name: "RemovedLibraryEntries");

            // Deliberately no DropColumn for MissingVerificationCount (Issues) or
            // LastLibraryHealthVerifyUtc (AppSettings) - left in place as orphans on down-migrate,
            // same pattern (and for the same reason) as 20260905042026_AddNavRailHoverExpandEnabled:
            // DropColumn on either table triggers SQLite's full-table-rebuild path, which rebuilds
            // from the *previous* migration's model snapshot - a snapshot that, because
            // UnifyLibrarySortGroupFields unmapped LibraryGroupField/LibrarySortField/
            // LibrarySortDirection from AppSettings without physically dropping them, no longer lists
            // those three orphaned columns. The rebuild would silently drop them as a side effect and
            // break any earlier Down() step in the rollback chain that still expects them (confirmed
            // real via the 2026-09-06 orphan-column migration-rollback bug fix).
        }
    }
}
