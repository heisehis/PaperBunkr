using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventOrderEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Designation",
                table: "Continuities",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventOrderEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ListKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Rank = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviousName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    NextName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventOrderEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventOrderEvidence_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventOrderEvidence_StoryEventId_Provider",
                table: "EventOrderEvidence",
                columns: new[] { "StoryEventId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventOrderEvidence");

            migrationBuilder.DropColumn(
                name: "Designation",
                table: "Continuities");
        }
    }
}
