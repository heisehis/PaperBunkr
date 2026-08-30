using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetKind",
                table: "SmartLists",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Issue");

            migrationBuilder.AddColumn<int>(
                name: "IssueSmartListId",
                table: "Collections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NovelSmartListId",
                table: "Collections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesSmartListId",
                table: "Collections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_IssueSmartListId",
                table: "Collections",
                column: "IssueSmartListId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_NovelSmartListId",
                table: "Collections",
                column: "NovelSmartListId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_SeriesSmartListId",
                table: "Collections",
                column: "SeriesSmartListId");

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_SmartLists_IssueSmartListId",
                table: "Collections",
                column: "IssueSmartListId",
                principalTable: "SmartLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_SmartLists_NovelSmartListId",
                table: "Collections",
                column: "NovelSmartListId",
                principalTable: "SmartLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_SmartLists_SeriesSmartListId",
                table: "Collections",
                column: "SeriesSmartListId",
                principalTable: "SmartLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_SmartLists_IssueSmartListId",
                table: "Collections");

            migrationBuilder.DropForeignKey(
                name: "FK_Collections_SmartLists_NovelSmartListId",
                table: "Collections");

            migrationBuilder.DropForeignKey(
                name: "FK_Collections_SmartLists_SeriesSmartListId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_IssueSmartListId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_NovelSmartListId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_SeriesSmartListId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "TargetKind",
                table: "SmartLists");

            migrationBuilder.DropColumn(
                name: "IssueSmartListId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "NovelSmartListId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "SeriesSmartListId",
                table: "Collections");
        }
    }
}
