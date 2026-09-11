using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderBackgroundTextureAndSpreadPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpreadPosition",
                table: "IssuePages",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundTexture",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - same pattern (and reason) as AddReaderMemoryLimitMb /
            // AddNavRailHoverExpandEnabled / AddBehaviorSettingsBatch2 / AddMetadataWriteBackSettings:
            // a DropColumn triggers SQLite's full-table-rebuild, which rebuilds from an earlier model
            // snapshot that no longer lists other columns unmapped-but-not-physically-dropped by
            // later migrations, silently dropping them and breaking a subsequent Down() step. Both
            // columns are left as harmless orphans on down-migrate. (memory:
            // project_paperbunkr_migration_rollback_orphan_column_bug)
        }
    }
}
