using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComicProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchedSeries_ComicVineVolumeId",
                table: "WatchedSeries");

            migrationBuilder.DropIndex(
                name: "IX_WantedIssues_ComicVineIssueId",
                table: "WantedIssues");

            migrationBuilder.DropIndex(
                name: "IX_ComicVineMatchMemories_SearchKey_ChosenVolumeId",
                table: "ComicVineMatchMemories");

            migrationBuilder.DropIndex(
                name: "IX_CatalogIssues_ComicVineIssueId",
                table: "CatalogIssues");

            migrationBuilder.RenameColumn(
                name: "ComicVineVolumeId",
                table: "WatchedSeries",
                newName: "ExternalVolumeId");

            migrationBuilder.RenameColumn(
                name: "ComicVineIssueId",
                table: "WantedIssues",
                newName: "ExternalIssueId");

            migrationBuilder.RenameColumn(
                name: "ComicVineIssueId",
                table: "CatalogIssues",
                newName: "ExternalIssueId");

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "WatchedSeries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "WantedIssues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "ComicVineMatchMemories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "CatalogIssues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedSeries_Provider_ExternalVolumeId",
                table: "WatchedSeries",
                columns: new[] { "Provider", "ExternalVolumeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WantedIssues_Provider_ExternalIssueId",
                table: "WantedIssues",
                columns: new[] { "Provider", "ExternalIssueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComicVineMatchMemories_Provider_SearchKey_ChosenVolumeId",
                table: "ComicVineMatchMemories",
                columns: new[] { "Provider", "SearchKey", "ChosenVolumeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogIssues_Provider_ExternalIssueId",
                table: "CatalogIssues",
                columns: new[] { "Provider", "ExternalIssueId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchedSeries_Provider_ExternalVolumeId",
                table: "WatchedSeries");

            migrationBuilder.DropIndex(
                name: "IX_WantedIssues_Provider_ExternalIssueId",
                table: "WantedIssues");

            migrationBuilder.DropIndex(
                name: "IX_ComicVineMatchMemories_Provider_SearchKey_ChosenVolumeId",
                table: "ComicVineMatchMemories");

            migrationBuilder.DropIndex(
                name: "IX_CatalogIssues_Provider_ExternalIssueId",
                table: "CatalogIssues");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "WatchedSeries");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "ComicVineMatchMemories");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "CatalogIssues");

            migrationBuilder.RenameColumn(
                name: "ExternalVolumeId",
                table: "WatchedSeries",
                newName: "ComicVineVolumeId");

            migrationBuilder.RenameColumn(
                name: "ExternalIssueId",
                table: "WantedIssues",
                newName: "ComicVineIssueId");

            migrationBuilder.RenameColumn(
                name: "ExternalIssueId",
                table: "CatalogIssues",
                newName: "ComicVineIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedSeries_ComicVineVolumeId",
                table: "WatchedSeries",
                column: "ComicVineVolumeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WantedIssues_ComicVineIssueId",
                table: "WantedIssues",
                column: "ComicVineIssueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComicVineMatchMemories_SearchKey_ChosenVolumeId",
                table: "ComicVineMatchMemories",
                columns: new[] { "SearchKey", "ChosenVolumeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogIssues_ComicVineIssueId",
                table: "CatalogIssues",
                column: "ComicVineIssueId",
                unique: true);
        }
    }
}
