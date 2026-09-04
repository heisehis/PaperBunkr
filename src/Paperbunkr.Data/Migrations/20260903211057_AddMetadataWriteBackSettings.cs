using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataWriteBackSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WriteMetadataAutomatically",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WriteMetadataToFiles",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WriteNativeSidecar",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - the three columns are left in place as orphans on down-migrate
            // rather than dropped, the same pattern (and for the same shared-dev-DB reason) as
            // 20260903133114_UnifyLibrarySortGroupFields. A DropColumn here would also be actively
            // harmful: EF's SQLite DropColumn rebuilds the whole AppSettings table using the
            // *previous* migration's model snapshot, which no longer lists the columns
            // UnifyLibrarySortGroupFields orphaned (LibrarySortField/LibrarySortDirection/
            // LibraryGroupField) - so the rebuild would silently drop those orphans, and a later
            // Down() step (20260817043912_AddLibraryListLayoutSettings) that explicitly drops
            // LibraryGroupField would then fail with "no such column". EF's own migration
            // round-trip tests exercise exactly this path.
        }
    }
}
