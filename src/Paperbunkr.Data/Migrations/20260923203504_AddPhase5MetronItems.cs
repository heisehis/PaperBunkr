using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase5MetronItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FocDate",
                table: "PullListReleases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommunityRatingCount",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IssueVariantCovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueVariantCovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueVariantCovers_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesAssociations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalSeriesId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesAssociations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesAssociations_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueVariantCovers_IssueId_ImageUrl",
                table: "IssueVariantCovers",
                columns: new[] { "IssueId", "ImageUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesAssociations_SeriesId_Provider_ExternalSeriesId",
                table: "SeriesAssociations",
                columns: new[] { "SeriesId", "Provider", "ExternalSeriesId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Issues.CommunityRatingCount is left in place as an orphan on down-migrate, not dropped: a change to Issues
            // triggers SQLite's full-table rebuild from the previous model snapshot, which still
            // expects the unmapped LibraryGroupField/LibrarySortField columns and fails with
            // "no such column: LibraryGroupField" (same rule and reason as
            // 20260905064447_AddIssueDuplicateAcknowledged).
            migrationBuilder.DropTable(
                name: "IssueVariantCovers");

            migrationBuilder.DropTable(
                name: "SeriesAssociations");

            migrationBuilder.DropColumn(
                name: "FocDate",
                table: "PullListReleases");

        }
    }
}
