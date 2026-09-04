using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBehaviorSettingsBatch2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableDragDropImport",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PromptReviewOnFinish",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RestoreSessionOnStartup",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ScanFoldersOnStartup",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - the four columns are left in place as orphans on down-migrate
            // rather than dropped, the same pattern (and for the same shared-dev-DB reason) as
            // 20260903211057_AddMetadataWriteBackSettings. EF's SQLite DropColumn rebuilds the whole
            // AppSettings table from the *previous* migration's model snapshot, which - because
            // AddActivityRuns and AddCoverAspectRatio were authored on parallel branches - is not a
            // reliable base to rebuild from here.
        }
    }
}
