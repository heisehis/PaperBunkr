using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuityFandomKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FandomKey",
                table: "Continuities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContinuityFandomSuggestionDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    FandomKey = table.Column<string>(type: "TEXT", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuityFandomSuggestionDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContinuityFandomSuggestionDismissals_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContinuityFandomSuggestionDismissals_SeriesId_FandomKey",
                table: "ContinuityFandomSuggestionDismissals",
                columns: new[] { "SeriesId", "FandomKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContinuityFandomSuggestionDismissals");

            migrationBuilder.DropColumn(
                name: "FandomKey",
                table: "Continuities");
        }
    }
}
