using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class MetadataModelPhase3MediaRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRelations_Series_SourceSeriesId",
                        column: x => x.SourceSeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaRelations_Series_TargetSeriesId",
                        column: x => x.TargetSeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaRelationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProviderRelationType = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderSourceId = table.Column<string>(type: "TEXT", nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    RetrievedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelationEvidence_MediaRelations_MediaRelationId",
                        column: x => x.MediaRelationId,
                        principalTable: "MediaRelations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaRelations_SourceSeriesId",
                table: "MediaRelations",
                column: "SourceSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRelations_TargetSeriesId",
                table: "MediaRelations",
                column: "TargetSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationEvidence_MediaRelationId",
                table: "RelationEvidence",
                column: "MediaRelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelationEvidence");

            migrationBuilder.DropTable(
                name: "MediaRelations");
        }
    }
}
