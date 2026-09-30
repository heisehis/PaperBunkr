using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesRelationDismissals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeriesRelationDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LowerSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    HigherSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesRelationDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesRelationDismissals_Series_HigherSeriesId",
                        column: x => x.HigherSeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesRelationDismissals_Series_LowerSeriesId",
                        column: x => x.LowerSeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesRelationDismissals_HigherSeriesId",
                table: "SeriesRelationDismissals",
                column: "HigherSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesRelationDismissals_LowerSeriesId_HigherSeriesId",
                table: "SeriesRelationDismissals",
                columns: new[] { "LowerSeriesId", "HigherSeriesId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeriesRelationDismissals");
        }
    }
}
