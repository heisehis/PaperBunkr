using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesRelationConnector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComicVineSeriesId",
                table: "SeriesAssociations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GcdSeriesId",
                table: "SeriesAssociations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IdsCheckedUtc",
                table: "SeriesAssociations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetronAssociationsCheckedUtc",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FindRelatedSeriesAutomatically",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComicVineSeriesId",
                table: "SeriesAssociations");

            migrationBuilder.DropColumn(
                name: "GcdSeriesId",
                table: "SeriesAssociations");

            migrationBuilder.DropColumn(
                name: "IdsCheckedUtc",
                table: "SeriesAssociations");

            // Series.MetronAssociationsCheckedUtc and AppSettings.FindRelatedSeriesAutomatically are left as orphan columns on purpose:
            // a SQLite DropColumn rebuilds the whole table from this snapshot, which silently drops older orphan columns and breaks a
            // later Down() in the rollback chain (same rule as AddNavRailHoverExpandEnabled / AddLastCoverVerificationUtc).
        }
    }
}
