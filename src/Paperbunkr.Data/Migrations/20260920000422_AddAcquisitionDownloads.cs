using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Download tracking, import and follow-arc support for comic acquisition (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md):
    /// the ReleaseBlocklist table, WantedIssue download/failure columns, AcquisitionSettings auto-grab/import columns and ReadingList.FollowArc.
    /// Purely additive (new columns carry defaults; nothing existing is altered or dropped), so an older build keeps working against this database.
    /// </summary>
    public partial class AddAcquisitionDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DownloadProgress",
                table: "WantedIssues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "WantedIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrabbedTitle",
                table: "WantedIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImportedAt",
                table: "WantedIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FollowArc",
                table: "ReadingLists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFollowedAt",
                table: "ReadingLists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoGrab",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoGrabMinScore",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<bool>(
                name: "MoveOriginalOnImport",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RenameTemplate",
                table: "AcquisitionSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{publisher}/{series} ({volumeyear})/{series} #{number:000}");

            migrationBuilder.AddColumn<bool>(
                name: "WriteComicInfo",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ReleaseBlocklist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReleaseName = table.Column<string>(type: "TEXT", nullable: false),
                    TorrentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseBlocklist", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseBlocklist_TorrentHash",
                table: "ReleaseBlocklist",
                column: "TorrentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseBlocklist");

            migrationBuilder.DropColumn(
                name: "DownloadProgress",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "GrabbedTitle",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "ImportedAt",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "FollowArc",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "LastFollowedAt",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "AutoGrab",
                table: "AcquisitionSettings");

            migrationBuilder.DropColumn(
                name: "AutoGrabMinScore",
                table: "AcquisitionSettings");

            migrationBuilder.DropColumn(
                name: "MoveOriginalOnImport",
                table: "AcquisitionSettings");

            migrationBuilder.DropColumn(
                name: "RenameTemplate",
                table: "AcquisitionSettings");

            migrationBuilder.DropColumn(
                name: "WriteComicInfo",
                table: "AcquisitionSettings");
        }
    }
}
