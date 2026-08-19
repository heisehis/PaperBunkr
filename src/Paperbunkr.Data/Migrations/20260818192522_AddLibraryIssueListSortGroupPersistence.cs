using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryIssueListSortGroupPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LibraryIssueListGroupField",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "LibraryIssueListSortDirection",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Descending");

            migrationBuilder.AddColumn<string>(
                name: "LibraryIssueListSortField",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Added");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LibraryIssueListGroupField",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryIssueListSortDirection",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryIssueListSortField",
                table: "AppSettings");
        }
    }
}
