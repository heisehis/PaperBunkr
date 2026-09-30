using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryEventIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IdentityCheckedAt",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityConflict",
                table: "StoryEvents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityMemberKey",
                table: "StoryEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "StoryEvents",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "User");

            // Existing events (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md, Q13): only the accept/look-up paths ever
            // set an arc id, so an event with one came from provider data. Everything else stays User - at worst a real duplicate is
            // reviewed instead of merged silently.
            migrationBuilder.Sql("UPDATE StoryEvents SET Origin = 'Provider' WHERE ComicVineArcId IS NOT NULL OR MetronArcId IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "StoryEventAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryEventAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryEventAliases_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryEventDuplicateDismissals",
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
                    table.PrimaryKey("PK_StoryEventDuplicateDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryEventDuplicateDismissals_StoryEvents_HigherEventId",
                        column: x => x.HigherEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoryEventDuplicateDismissals_StoryEvents_LowerEventId",
                        column: x => x.LowerEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventAliases_Key",
                table: "StoryEventAliases",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventAliases_StoryEventId_Key",
                table: "StoryEventAliases",
                columns: new[] { "StoryEventId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventDuplicateDismissals_HigherEventId",
                table: "StoryEventDuplicateDismissals",
                column: "HigherEventId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventDuplicateDismissals_LowerEventId_HigherEventId",
                table: "StoryEventDuplicateDismissals",
                columns: new[] { "LowerEventId", "HigherEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryEventAliases");

            migrationBuilder.DropTable(
                name: "StoryEventDuplicateDismissals");

            migrationBuilder.DropColumn(
                name: "IdentityCheckedAt",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "IdentityConflict",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "IdentityMemberKey",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "StoryEvents");
        }
    }
}
