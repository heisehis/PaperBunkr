using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class MetadataModelPhase1CanonicalMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited from the scaffolded migration (docs/superpowers/specs/2026-08-17-metadata-
            // model-phase1-canonical-metadata-design.md): the auto-generated version mistook the
            // BlackAndWhite->OpenCount column swap for a RenameColumn (same rough position/table),
            // which would have silently carried old bool 0/1 values into the new counter AND
            // destroyed the source data ColorMode's backfill below needs. Explicit Add/backfill/Drop
            // instead, in an order that reads the old columns before they're removed.

            migrationBuilder.AddColumn<string>(
                name: "ColorMode",
                table: "Issues",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            // Backfill from the outgoing bool BlackAndWhite before it's dropped below.
            migrationBuilder.Sql(
                "UPDATE Issues SET ColorMode = CASE WHEN BlackAndWhite = 1 THEN 'BlackAndWhite' ELSE 'Color' END;");

            migrationBuilder.DropColumn(
                name: "BlackAndWhite",
                table: "Issues");

            migrationBuilder.AddColumn<int>(
                name: "OpenCount",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Series",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            // Backfill from the outgoing bool IsComplete before it's dropped below: true->Completed,
            // false->Unknown (CE never told us anything stronger than "not marked complete").
            migrationBuilder.Sql(
                "UPDATE Series SET Status = CASE WHEN IsComplete = 1 THEN 'Completed' ELSE 'Unknown' END;");

            migrationBuilder.DropColumn(
                name: "IsComplete",
                table: "Series");

            migrationBuilder.AlterColumn<string>(
                name: "Volume",
                table: "Issues",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinalIssue",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "IssueBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueBookmarks_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueBookmarks_IssueId",
                table: "IssueBookmarks",
                column: "IssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueBookmarks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "ColorMode",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "IsFinalIssue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "OpenCount",
                table: "Issues");

            migrationBuilder.AddColumn<bool>(
                name: "BlackAndWhite",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsComplete",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "Volume",
                table: "Issues",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
