using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryHealthConfirmedMissingThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LibraryHealthConfirmedMissingThreshold",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately no DropColumn - AppSettings/Issues column drops trigger SQLite's
            // full-table-rebuild path, which rebuilds from the *previous* migration's model
            // snapshot and can silently drop other already-orphaned columns as a side effect,
            // breaking earlier Down() steps in the rollback chain (confirmed real via the
            // 2026-09-06 orphan-column migration-rollback bug fix - see
            // 20260906153921_AddMissingVerificationCountAndRemovedLibraryEntry.cs's Down() for the
            // same reasoning). Left in place as an orphan on down-migrate.
        }
    }
}
