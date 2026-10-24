using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryEventTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoryEventTypeAssignments",
                columns: table => new
                {
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryEventTypeAssignments", x => new { x.StoryEventId, x.Type });
                    table.ForeignKey(
                        name: "FK_StoryEventTypeAssignments_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventTypeAssignments_Type",
                table: "StoryEventTypeAssignments",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryEventTypeAssignments");
        }
    }
}
