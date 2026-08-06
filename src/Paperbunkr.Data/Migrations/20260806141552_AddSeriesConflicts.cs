using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeriesConflicts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExistingSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesAId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesBId = table.Column<int>(type: "INTEGER", nullable: true),
                    IncomingName = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedName = table.Column<string>(type: "TEXT", nullable: false),
                    Similarity = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesConflicts_Series_ExistingSeriesId",
                        column: x => x.ExistingSeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeriesConflicts_Series_SeriesAId",
                        column: x => x.SeriesAId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeriesConflicts_Series_SeriesBId",
                        column: x => x.SeriesBId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeriesConflicts_ExistingSeriesId",
                table: "SeriesConflicts",
                column: "ExistingSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesConflicts_SeriesAId",
                table: "SeriesConflicts",
                column: "SeriesAId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesConflicts_SeriesBId",
                table: "SeriesConflicts",
                column: "SeriesBId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesConflicts_Status",
                table: "SeriesConflicts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeriesConflicts");
        }
    }
}
