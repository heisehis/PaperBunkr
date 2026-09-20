using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyPullList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PullListRefreshedAt",
                table: "AcquisitionSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MetronSeries",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    YearBegan = table.Column<int>(type: "INTEGER", nullable: true),
                    ComicVineId = table.Column<int>(type: "INTEGER", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetronSeries", x => x.SeriesId);
                });

            migrationBuilder.CreateTable(
                name: "PullListReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalIssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IssueNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StoreDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CoverDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullListReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullListReleases_ExternalIssueId",
                table: "PullListReleases",
                column: "ExternalIssueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullListReleases_StoreDate",
                table: "PullListReleases",
                column: "StoreDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetronSeries");

            migrationBuilder.DropTable(
                name: "PullListReleases");

            migrationBuilder.DropColumn(
                name: "PullListRefreshedAt",
                table: "AcquisitionSettings");
        }
    }
}
