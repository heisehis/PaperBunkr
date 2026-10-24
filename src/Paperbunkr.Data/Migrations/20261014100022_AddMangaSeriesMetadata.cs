using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMangaSeriesMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeRating",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanonicalReviewVersion",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EmbeddedSyncEnabled",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Imprint",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedFields",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmbeddedDirty",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedFileStamp",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedFingerprint",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedSyncMessage",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedSyncState",
                table: "Issues",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "IssueFieldOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueFieldOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueFieldOverrides_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueMetadataSnapshots",
                columns: table => new
                {
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TakenUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueMetadataSnapshots", x => x.IssueId);
                    table.ForeignKey(
                        name: "FK_IssueMetadataSnapshots_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesCredits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesCredits_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesTagRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesTagRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeriesTagRows_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_EmbeddedDirty",
                table: "Issues",
                column: "EmbeddedDirty");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFieldOverrides_IssueId_FieldKey",
                table: "IssueFieldOverrides",
                columns: new[] { "IssueId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeriesCredits_SeriesId",
                table: "SeriesCredits",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesTagRows_SeriesId",
                table: "SeriesTagRows",
                column: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueFieldOverrides");

            migrationBuilder.DropTable(
                name: "IssueMetadataSnapshots");

            migrationBuilder.DropTable(
                name: "SeriesCredits");

            migrationBuilder.DropTable(
                name: "SeriesTagRows");

            migrationBuilder.DropIndex(
                name: "IX_Issues_EmbeddedDirty",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "AgeRating",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "CanonicalReviewVersion",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "EmbeddedSyncEnabled",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Imprint",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "LockedFields",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "EmbeddedDirty",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "EmbeddedFileStamp",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "EmbeddedFingerprint",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "EmbeddedSyncMessage",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "EmbeddedSyncState",
                table: "Issues");
        }
    }
}
