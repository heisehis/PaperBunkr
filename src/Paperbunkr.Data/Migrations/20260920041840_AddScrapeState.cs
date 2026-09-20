using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScrapeAttempts",
                table: "WantedIssues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScrapeError",
                table: "WantedIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ScrapeFailureIsTerminal",
                table: "WantedIssues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScrapeLastAttemptAt",
                table: "WantedIssues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScrapeStatus",
                table: "WantedIssues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ScrapeOnImport",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScrapeAttempts",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "ScrapeError",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "ScrapeFailureIsTerminal",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "ScrapeLastAttemptAt",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "ScrapeStatus",
                table: "WantedIssues");

            migrationBuilder.DropColumn(
                name: "ScrapeOnImport",
                table: "AcquisitionSettings");
        }
    }
}
