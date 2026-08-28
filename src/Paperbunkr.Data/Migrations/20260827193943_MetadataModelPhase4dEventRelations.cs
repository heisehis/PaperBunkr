using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class MetadataModelPhase4dEventRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRelations_StoryEvents_SourceEventId",
                        column: x => x.SourceEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventRelations_StoryEvents_TargetEventId",
                        column: x => x.TargetEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventRelationEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventRelationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProviderRelationType = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderSourceId = table.Column<string>(type: "TEXT", nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    RetrievedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRelationEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRelationEvidence_EventRelations_EventRelationId",
                        column: x => x.EventRelationId,
                        principalTable: "EventRelations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRelationEvidence_EventRelationId",
                table: "EventRelationEvidence",
                column: "EventRelationId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRelations_SourceEventId",
                table: "EventRelations",
                column: "SourceEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRelations_TargetEventId",
                table: "EventRelations",
                column: "TargetEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRelationEvidence");

            migrationBuilder.DropTable(
                name: "EventRelations");
        }
    }
}
