using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderMemoryLimitMb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReaderMemoryLimitMb",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - same pattern (and reason) as AddNavRailHoverExpandEnabled /
            // AddBehaviorSettingsBatch2 / AddMetadataWriteBackSettings: a DropColumn on AppSettings
            // triggers SQLite's full-table-rebuild, which rebuilds from an earlier model snapshot
            // that no longer lists other columns unmapped-but-not-physically-dropped by later
            // migrations, silently dropping them and breaking a subsequent Down() step. The column
            // is left as a harmless orphan on down-migrate. (memory:
            // project_paperbunkr_migration_rollback_orphan_column_bug)
        }
    }
}
