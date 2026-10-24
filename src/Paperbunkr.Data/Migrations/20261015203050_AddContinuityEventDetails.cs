using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuityEventDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverSource",
                table: "StoryEvents",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTime>(
                name: "DetailsCheckedAt",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoryEventTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Weight = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryEventTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryEventTags_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventTags_StoryEventId_Category_Value",
                table: "StoryEventTags",
                columns: new[] { "StoryEventId", "Category", "Value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryEventTags");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "CoverSource",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "DetailsCheckedAt",
                table: "StoryEvents");
        }
    }
}
