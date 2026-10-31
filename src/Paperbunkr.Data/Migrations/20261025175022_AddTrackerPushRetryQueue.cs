using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackerPushRetryQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackerPushRetries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackingLinkId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    DesiredStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    HighestChapter = table.Column<int>(type: "INTEGER", nullable: true),
                    RetriesUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackerPushRetries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackerPushRetries_TrackingLinks_TrackingLinkId",
                        column: x => x.TrackingLinkId,
                        principalTable: "TrackingLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackerPushRetries_TrackingLinkId",
                table: "TrackerPushRetries",
                column: "TrackingLinkId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackerPushRetries");
        }
    }
}
