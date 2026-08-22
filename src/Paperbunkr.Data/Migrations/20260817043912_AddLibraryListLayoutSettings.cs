using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryListLayoutSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LibraryActiveCategoryId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LibraryActiveContentType",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryFilterMissingIssues",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryFilterTrackedOnly",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryFilterUnreadOnly",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "LibraryGridDensity",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<string>(
                name: "LibraryGroupField",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "LibrarySearchQuery",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryShowContinueReadingButton",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryShowLanguageBadge",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryShowPublisherBadge",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LibraryShowUnreadBadge",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LibrarySortDirection",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Descending");

            migrationBuilder.AddColumn<string>(
                name: "LibrarySortField",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "DateAdded");

            migrationBuilder.AddColumn<bool>(
                name: "LibraryUseLanguageIcon",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LibraryViewMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "ComfortableGrid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LibraryActiveCategoryId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryActiveContentType",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryFilterMissingIssues",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryFilterTrackedOnly",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryFilterUnreadOnly",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryGridDensity",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryGroupField",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibrarySearchQuery",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryShowContinueReadingButton",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryShowLanguageBadge",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryShowPublisherBadge",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryShowUnreadBadge",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibrarySortDirection",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibrarySortField",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryUseLanguageIcon",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryViewMode",
                table: "AppSettings");
        }
    }
}
