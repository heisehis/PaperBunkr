using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class MetadataModelPhase4cReadingListOverhaul : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ReadingLists",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<int>(
                name: "StoryEventId",
                table: "ReadingLists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "ReadingLists",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ReadingLists",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ReadingListItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "ReadingListItems",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_StoryEventId",
                table: "ReadingLists",
                column: "StoryEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingLists_StoryEvents_StoryEventId",
                table: "ReadingLists",
                column: "StoryEventId",
                principalTable: "StoryEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // SQLite rejects a non-constant (CURRENT_TIMESTAMP) default on ALTER TABLE ADD COLUMN,
            // so the AddColumn calls above backfill with a placeholder epoch instead - this raw SQL
            // immediately overwrites every existing row with the real migration-run time, the
            // actual intent (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-
            // list-overhaul-design.md: "backfilled to the migration's own run time").
            migrationBuilder.Sql("UPDATE ReadingLists SET CreatedAt = CURRENT_TIMESTAMP, UpdatedAt = CURRENT_TIMESTAMP;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadingLists_StoryEvents_StoryEventId",
                table: "ReadingLists");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_StoryEventId",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "StoryEventId",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ReadingListItems");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "ReadingListItems");
        }
    }
}
