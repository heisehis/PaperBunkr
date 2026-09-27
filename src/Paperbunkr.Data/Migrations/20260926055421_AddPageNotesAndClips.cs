using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageNotesAndClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InfoPanelShowSummary",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PageClips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RectX = table.Column<double>(type: "REAL", nullable: false),
                    RectY = table.Column<double>(type: "REAL", nullable: false),
                    RectWidth = table.Column<double>(type: "REAL", nullable: false),
                    RectHeight = table.Column<double>(type: "REAL", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageClips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageClips_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PageNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageNotes_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageClips_IssueId_PageNumber",
                table: "PageClips",
                columns: new[] { "IssueId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PageNotes_IssueId_PageNumber",
                table: "PageNotes",
                columns: new[] { "IssueId", "PageNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md): DropColumn rebuilds AppSettings and the rollback chain has orphaned columns before; an unused table or column left behind is harmless.
        }
    }
}
