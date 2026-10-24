using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingListItemSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingListItemSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingListItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Series = table.Column<string>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    Volume = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Format = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesKey = table.Column<string>(type: "TEXT", nullable: false),
                    NumberKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceEntryId = table.Column<string>(type: "TEXT", nullable: true),
                    ComicVineIssueId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ComicVineSeriesId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MetronIssueId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MetronSeriesId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastCheckedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingListItemSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingListItemSources_ReadingListItems_ReadingListItemId",
                        column: x => x.ReadingListItemId,
                        principalTable: "ReadingListItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListItemSources_ComicVineIssueId",
                table: "ReadingListItemSources",
                column: "ComicVineIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListItemSources_MetronIssueId",
                table: "ReadingListItemSources",
                column: "MetronIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListItemSources_ReadingListItemId",
                table: "ReadingListItemSources",
                column: "ReadingListItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListItemSources_SeriesKey_NumberKey",
                table: "ReadingListItemSources",
                columns: new[] { "SeriesKey", "NumberKey" });

            // Entries already waiting on a placeholder get a record from what the placeholder still holds (design §4, decision Q11).
            ReadingListItemSourceBackfill.Run(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingListItemSources");
        }
    }
}
