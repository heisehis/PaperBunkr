using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGcdIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GcdMatchSource",
                table: "Series",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GcdSeriesId",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GcdIssueId",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_GcdSeriesId",
                table: "Series",
                column: "GcdSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_GcdIssueId",
                table: "Issues",
                column: "GcdIssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Series_GcdSeriesId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Issues_GcdIssueId",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "GcdMatchSource",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "GcdSeriesId",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "GcdIssueId",
                table: "Issues");
        }
    }
}
