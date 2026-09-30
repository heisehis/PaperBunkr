using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventChronology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChronologyCheckedAt",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikidataQid",
                table: "StoryEvents",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventRelationDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LowerEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    HigherEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRelationDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRelationDismissals_StoryEvents_HigherEventId",
                        column: x => x.HigherEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventRelationDismissals_StoryEvents_LowerEventId",
                        column: x => x.LowerEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRelationDismissals_HigherEventId",
                table: "EventRelationDismissals",
                column: "HigherEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRelationDismissals_LowerEventId_HigherEventId",
                table: "EventRelationDismissals",
                columns: new[] { "LowerEventId", "HigherEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRelationDismissals");

            migrationBuilder.DropColumn(
                name: "ChronologyCheckedAt",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "WikidataQid",
                table: "StoryEvents");
        }
    }
}
