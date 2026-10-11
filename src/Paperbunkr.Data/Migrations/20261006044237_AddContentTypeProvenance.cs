using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentTypeProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContentTypeAutoAppliedUtc",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentTypeCheck",
                table: "Series",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTime>(
                name: "ContentTypeCheckedUtc",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ContentTypeConfidence",
                table: "Series",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentTypeEvidence",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContentTypeLocked",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContentTypeSource",
                table: "Series",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unset");

            migrationBuilder.AddColumn<string>(
                name: "ContentTypeSuggestion",
                table: "Series",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousContentType",
                table: "Series",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousReadingMode",
                table: "Series",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AskBeforeClassifying",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Lock every series that already has a type: provenance was never recorded, so a value set by hand, by a CE import, by the scanner
            // or by the publisher heuristic cannot be told apart, and none of them may be overridden. Unknown series stay unlocked.
            // (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md)
            migrationBuilder.Sql(
                "UPDATE \"Series\" SET \"ContentTypeLocked\" = 1, \"ContentTypeSource\" = 'Existing' WHERE \"ContentType\" <> 'Unknown';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op: on SQLite a DropColumn rebuilds the table and strands orphaned columns (see the rollback-chain bug in the notes).
        }
    }
}
