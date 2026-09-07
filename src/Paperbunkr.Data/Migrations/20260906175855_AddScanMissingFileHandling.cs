using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScanMissingFileHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRemoveMissingOnScan",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DontReimportRemovedFiles",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RemovedFilePaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemovedFilePaths", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemovedFilePaths_FilePath",
                table: "RemovedFilePaths",
                column: "FilePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // RemovedFilePaths is a brand-new table, safe to drop outright.
            migrationBuilder.DropTable(
                name: "RemovedFilePaths");

            // Deliberately no DropColumn for AutoRemoveMissingOnScan/DontReimportRemovedFiles
            // (AppSettings) - left in place as orphans on down-migrate, same standing rule (and for
            // the same reason) as 20260906153921_AddMissingVerificationCountAndRemovedLibraryEntry:
            // DropColumn on AppSettings triggers SQLite's full-table-rebuild path, which rebuilds
            // from the *previous* migration's model snapshot and can silently drop other columns
            // that snapshot no longer lists but the live table still has (the 2026-09-06 orphan-
            // column migration-rollback bug this rule exists to prevent).
        }
    }
}
