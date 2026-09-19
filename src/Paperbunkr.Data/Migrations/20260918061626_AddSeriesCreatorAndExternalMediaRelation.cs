using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesCreatorAndExternalMediaRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Creator",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalMediaRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTitle = table.Column<string>(type: "TEXT", nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", nullable: true),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalMediaRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalMediaRelations_Series_SourceSeriesId",
                        column: x => x.SourceSeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalMediaRelations_Provider_TargetExternalId",
                table: "ExternalMediaRelations",
                columns: new[] { "Provider", "TargetExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalMediaRelations_SourceSeriesId",
                table: "ExternalMediaRelations",
                column: "SourceSeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalMediaRelations");

            migrationBuilder.DropColumn(
                name: "Creator",
                table: "Series");
        }
    }
}
