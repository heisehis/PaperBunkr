using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseListProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullListReleases_ExternalIssueId",
                table: "PullListReleases");

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "PullListReleases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);      // every row saved so far came from Metron

            migrationBuilder.CreateTable(
                name: "ReleaseSeries",
                columns: table => new
                {
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    YearBegan = table.Column<int>(type: "INTEGER", nullable: true),
                    ComicVineId = table.Column<int>(type: "INTEGER", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseSeries", x => new { x.Provider, x.SeriesId });
                });

            // The cached series info is Metron's (the only source until now): carry it over so it is not fetched again.
            migrationBuilder.Sql(
                "INSERT INTO \"ReleaseSeries\" (\"Provider\", \"SeriesId\", \"Name\", \"Publisher\", \"YearBegan\", \"ComicVineId\", \"FetchedAt\") " +
                "SELECT 1, \"SeriesId\", \"Name\", \"Publisher\", \"YearBegan\", \"ComicVineId\", \"FetchedAt\" FROM \"MetronSeries\";");

            migrationBuilder.DropTable(
                name: "MetronSeries");

            migrationBuilder.CreateIndex(
                name: "IX_PullListReleases_Provider_ExternalIssueId",
                table: "PullListReleases",
                columns: new[] { "Provider", "ExternalIssueId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullListReleases_Provider_ExternalIssueId",
                table: "PullListReleases");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "PullListReleases");

            migrationBuilder.CreateTable(
                name: "MetronSeries",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComicVineId = table.Column<int>(type: "INTEGER", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    YearBegan = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetronSeries", x => x.SeriesId);
                });

            migrationBuilder.Sql(
                "INSERT INTO \"MetronSeries\" (\"SeriesId\", \"Name\", \"Publisher\", \"YearBegan\", \"ComicVineId\", \"FetchedAt\") " +
                "SELECT \"SeriesId\", \"Name\", \"Publisher\", \"YearBegan\", \"ComicVineId\", \"FetchedAt\" FROM \"ReleaseSeries\" WHERE \"Provider\" = 1;");

            migrationBuilder.DropTable(
                name: "ReleaseSeries");

            migrationBuilder.CreateIndex(
                name: "IX_PullListReleases_ExternalIssueId",
                table: "PullListReleases",
                column: "ExternalIssueId",
                unique: true);
        }
    }
}
