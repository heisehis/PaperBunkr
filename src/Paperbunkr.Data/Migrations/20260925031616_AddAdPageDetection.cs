using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdPageDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdPageHashes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hash = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceIssueId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourcePageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdPageHashes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageHashes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Hash = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentStamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageHashes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageHashes_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdPageProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedAdHashId = table.Column<int>(type: "INTEGER", nullable: true),
                    Distance = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdPageProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdPageProposals_AdPageHashes_MatchedAdHashId",
                        column: x => x.MatchedAdHashId,
                        principalTable: "AdPageHashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AdPageProposals_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdPageHashes_SourceIssueId_SourcePageNumber",
                table: "AdPageHashes",
                columns: new[] { "SourceIssueId", "SourcePageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AdPageProposals_IssueId_PageNumber",
                table: "AdPageProposals",
                columns: new[] { "IssueId", "PageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdPageProposals_MatchedAdHashId",
                table: "AdPageProposals",
                column: "MatchedAdHashId");

            migrationBuilder.CreateIndex(
                name: "IX_AdPageProposals_Status",
                table: "AdPageProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PageHashes_IssueId_PageNumber",
                table: "PageHashes",
                columns: new[] { "IssueId", "PageNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5): the
            // rollback chain has orphaned columns before; unused tables left behind are harmless.
        }
    }
}
