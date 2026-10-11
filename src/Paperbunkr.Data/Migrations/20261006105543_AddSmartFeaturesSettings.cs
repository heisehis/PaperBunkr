using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartFeaturesSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedNotifiedAt",
                table: "StoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedNotifiedAt",
                table: "Continuities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoApplyMinConfidence",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "RefreshProviderDataOnComplete",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedNotifiedAt",
                table: "StoryEvents");

            migrationBuilder.DropColumn(
                name: "CompletedNotifiedAt",
                table: "Continuities");

            // AutoApplyMinConfidence and RefreshProviderDataOnComplete are deliberately left in place. On SQLite a DropColumn rebuilds the
            // whole table from the model, and AppSettings still carries three columns the model no longer maps (LibraryGroupField,
            // LibrarySortField, LibrarySortDirection); the rebuild would silently drop them and break every older Down() that expects
            // them (the rollback-chain bug in the notes; same rule as AddNavRailHoverExpandEnabled and AddContentTypeProvenance).
        }
    }
}
