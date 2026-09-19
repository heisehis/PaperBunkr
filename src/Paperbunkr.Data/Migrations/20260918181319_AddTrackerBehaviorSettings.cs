using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackerBehaviorSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrackerPromptShown",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrackerAutoOpenLinkPanel",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackerAutoSyncFromTrackers",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrackerUpdateAfterReading",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackerUpdateOnMarkRead",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Always");

            migrationBuilder.AddColumn<bool>(
                name: "TrackerUseSourceMetadata",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - columns are left in place as orphans on down-migrate rather
            // than dropped: EF's SQLite DropColumn rebuilds the whole table from the previous
            // migration's model snapshot, the rollback-chain hazard every AppSettings migration
            // here (e.g. 20260904045621_AddBehaviorSettingsBatch2) sidesteps the same way.
        }
    }
}
