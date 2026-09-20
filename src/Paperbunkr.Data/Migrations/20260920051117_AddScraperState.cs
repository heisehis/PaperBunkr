using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScraperState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComicVineMatchMemories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SearchKey = table.Column<string>(type: "TEXT", nullable: false),
                    ChosenVolumeId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicVineMatchMemories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeSettingsRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeSettingsRows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComicVineMatchMemories_SearchKey_ChosenVolumeId",
                table: "ComicVineMatchMemories",
                columns: new[] { "SearchKey", "ChosenVolumeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComicVineMatchMemories");

            migrationBuilder.DropTable(
                name: "ScrapeSettingsRows");
        }
    }
}
