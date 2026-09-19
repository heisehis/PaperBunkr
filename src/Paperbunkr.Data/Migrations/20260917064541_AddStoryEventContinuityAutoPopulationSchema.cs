using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryEventContinuityAutoPopulationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComicVineArcId",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetronArcId",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikidataId",
                table: "Continuities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContinuityCharacterLookupNegativeCaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuityCharacterLookupNegativeCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContinuityCharacterLookupNegativeCaches_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContinuitySuggestionDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    WikidataQid = table.Column<string>(type: "TEXT", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuitySuggestionDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContinuitySuggestionDismissals_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryEventCandidateDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArcName = table.Column<string>(type: "TEXT", nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryEventCandidateDismissals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoryEventVerificationNegativeCaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArcName = table.Column<string>(type: "TEXT", nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryEventVerificationNegativeCaches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContinuityCharacterLookupNegativeCaches_CharacterId",
                table: "ContinuityCharacterLookupNegativeCaches",
                column: "CharacterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContinuitySuggestionDismissals_SeriesId_WikidataQid",
                table: "ContinuitySuggestionDismissals",
                columns: new[] { "SeriesId", "WikidataQid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventCandidateDismissals_ArcName_Publisher",
                table: "StoryEventCandidateDismissals",
                columns: new[] { "ArcName", "Publisher" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryEventVerificationNegativeCaches_ArcName_Publisher_Source",
                table: "StoryEventVerificationNegativeCaches",
                columns: new[] { "ArcName", "Publisher", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContinuityCharacterLookupNegativeCaches");

            migrationBuilder.DropTable(
                name: "ContinuitySuggestionDismissals");

            migrationBuilder.DropTable(
                name: "StoryEventCandidateDismissals");

            migrationBuilder.DropTable(
                name: "StoryEventVerificationNegativeCaches");

            migrationBuilder.DropColumn(
                name: "ComicVineArcId",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "MetronArcId",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "WikidataId",
                table: "Continuities");
        }
    }
}
