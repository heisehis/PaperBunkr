using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategorySeries",
                columns: table => new
                {
                    CategoriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorySeries", x => new { x.CategoriesId, x.SeriesId });
                    table.ForeignKey(
                        name: "FK_CategorySeries_Categories_CategoriesId",
                        column: x => x.CategoriesId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Number = table.Column<string>(type: "TEXT", nullable: true),
                    Count = table.Column<int>(type: "INTEGER", nullable: true),
                    Volume = table.Column<int>(type: "INTEGER", nullable: true),
                    AlternateSeries = table.Column<string>(type: "TEXT", nullable: true),
                    AlternateNumber = table.Column<string>(type: "TEXT", nullable: true),
                    StoryArc = table.Column<string>(type: "TEXT", nullable: true),
                    StoryArcNumber = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesGroup = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Review = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    Day = table.Column<int>(type: "INTEGER", nullable: true),
                    Writer = table.Column<string>(type: "TEXT", nullable: true),
                    Penciller = table.Column<string>(type: "TEXT", nullable: true),
                    Inker = table.Column<string>(type: "TEXT", nullable: true),
                    Colorist = table.Column<string>(type: "TEXT", nullable: true),
                    Letterer = table.Column<string>(type: "TEXT", nullable: true),
                    CoverArtist = table.Column<string>(type: "TEXT", nullable: true),
                    Editor = table.Column<string>(type: "TEXT", nullable: true),
                    Translator = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    Imprint = table.Column<string>(type: "TEXT", nullable: true),
                    Genre = table.Column<string>(type: "TEXT", nullable: true),
                    Web = table.Column<string>(type: "TEXT", nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LanguageISO = table.Column<string>(type: "TEXT", nullable: true),
                    Format = table.Column<string>(type: "TEXT", nullable: true),
                    AgeRating = table.Column<string>(type: "TEXT", nullable: true),
                    Characters = table.Column<string>(type: "TEXT", nullable: true),
                    Teams = table.Column<string>(type: "TEXT", nullable: true),
                    Locations = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    ReadingModeOverride = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    AddedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleasedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OpenedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastPageRead = table.Column<int>(type: "INTEGER", nullable: true),
                    FileIsMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    CustomThumbnailKey = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortName = table.Column<string>(type: "TEXT", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReadingMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    Genre = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    CoverIssueId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Issues_CoverIssueId",
                        column: x => x.CoverIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrackingLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Service = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    LastSyncedIssueNumber = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackingLinks_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategorySeries_SeriesId",
                table: "CategorySeries",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_FilePath",
                table: "Issues",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_SeriesId",
                table: "Issues",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_CoverIssueId",
                table: "Series",
                column: "CoverIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_Name",
                table: "Series",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingLinks_SeriesId_Service_ExternalId",
                table: "TrackingLinks",
                columns: new[] { "SeriesId", "Service", "ExternalId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CategorySeries_Series_SeriesId",
                table: "CategorySeries",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_Series_SeriesId",
                table: "Issues",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Issues_Series_SeriesId",
                table: "Issues");

            migrationBuilder.DropTable(
                name: "CategorySeries");

            migrationBuilder.DropTable(
                name: "TrackingLinks");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "Issues");
        }
    }
}
