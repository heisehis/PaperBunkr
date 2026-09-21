using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcquisitionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProwlarrUrl = table.Column<string>(type: "TEXT", nullable: false),
                    QBittorrentUrl = table.Column<string>(type: "TEXT", nullable: false),
                    QBittorrentCategory = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationFolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    PollIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MinSizeMb = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxSizeMb = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredReleaseGroups = table.Column<string>(type: "TEXT", nullable: false),
                    IgnoredWords = table.Column<string>(type: "TEXT", nullable: false),
                    PreferCbz = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchedSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComicVineVolumeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StartYear = table.Column<int>(type: "INTEGER", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    WatchFutureReleases = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRefreshedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchedSeries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchedSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComicVineIssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    StoreDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CoverDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogIssues_WatchedSeries_WatchedSeriesId",
                        column: x => x.WatchedSeriesId,
                        principalTable: "WatchedSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WantedIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WatchedSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComicVineIssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    StoreDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TorrentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSearchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WantedIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WantedIssues_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WantedIssues_WatchedSeries_WatchedSeriesId",
                        column: x => x.WatchedSeriesId,
                        principalTable: "WatchedSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WantedIssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Seeders = table.Column<int>(type: "INTEGER", nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", nullable: true),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    IsPack = table.Column<bool>(type: "INTEGER", nullable: false),
                    FoundAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseCandidates_WantedIssues_WantedIssueId",
                        column: x => x.WantedIssueId,
                        principalTable: "WantedIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogIssues_ComicVineIssueId",
                table: "CatalogIssues",
                column: "ComicVineIssueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogIssues_WatchedSeriesId",
                table: "CatalogIssues",
                column: "WatchedSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCandidates_WantedIssueId",
                table: "ReleaseCandidates",
                column: "WantedIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_WantedIssues_ComicVineIssueId",
                table: "WantedIssues",
                column: "ComicVineIssueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WantedIssues_IssueId",
                table: "WantedIssues",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_WantedIssues_Status",
                table: "WantedIssues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WantedIssues_WatchedSeriesId",
                table: "WantedIssues",
                column: "WatchedSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedSeries_ComicVineVolumeId",
                table: "WatchedSeries",
                column: "ComicVineVolumeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedSeries_SeriesId",
                table: "WatchedSeries",
                column: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcquisitionSettings");

            migrationBuilder.DropTable(
                name: "CatalogIssues");

            migrationBuilder.DropTable(
                name: "ReleaseCandidates");

            migrationBuilder.DropTable(
                name: "WantedIssues");

            migrationBuilder.DropTable(
                name: "WatchedSeries");
        }
    }
}
