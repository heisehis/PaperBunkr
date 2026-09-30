using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingListFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContinuityId",
                table: "ReadingLists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContinuityOrderKind",
                table: "ReadingLists",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FolderId",
                table: "ReadingLists",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadingListFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ParentFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCollapsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingListFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingListFolders_ReadingListFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "ReadingListFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReadingListOverlapDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ListAId = table.Column<int>(type: "INTEGER", nullable: false),
                    ListBId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingListOverlapDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingListOverlapDismissals_ReadingLists_ListAId",
                        column: x => x.ListAId,
                        principalTable: "ReadingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingListOverlapDismissals_ReadingLists_ListBId",
                        column: x => x.ListBId,
                        principalTable: "ReadingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_ContinuityId",
                table: "ReadingLists",
                column: "ContinuityId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_FolderId",
                table: "ReadingLists",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListFolders_ParentFolderId",
                table: "ReadingListFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListOverlapDismissals_ListAId_ListBId",
                table: "ReadingListOverlapDismissals",
                columns: new[] { "ListAId", "ListBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListOverlapDismissals_ListBId",
                table: "ReadingListOverlapDismissals",
                column: "ListBId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingLists_Continuities_ContinuityId",
                table: "ReadingLists",
                column: "ContinuityId",
                principalTable: "Continuities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingLists_ReadingListFolders_FolderId",
                table: "ReadingLists",
                column: "FolderId",
                principalTable: "ReadingListFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // SortOrder now means "order among the lists in the same folder" and drag-reorder relies on it being distinct. CBL/CSV
            // imports left it at 0, so renumber every existing list 0..n in its current order (SortOrder, then Id).
            migrationBuilder.Sql(ReadingListSortOrderRenumberSql);
        }

        // Snapshot the new positions first: a correlated subquery over ReadingLists itself would see rows this UPDATE already changed.
        internal const string ReadingListSortOrderRenumberSql =
            "CREATE TEMP TABLE _ReadingListOrder AS SELECT Id, ROW_NUMBER() OVER (ORDER BY SortOrder, Id) - 1 AS Position FROM ReadingLists; " +
            "UPDATE ReadingLists SET SortOrder = (SELECT Position FROM _ReadingListOrder o WHERE o.Id = ReadingLists.Id); " +
            "DROP TABLE _ReadingListOrder;";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadingLists_Continuities_ContinuityId",
                table: "ReadingLists");

            migrationBuilder.DropForeignKey(
                name: "FK_ReadingLists_ReadingListFolders_FolderId",
                table: "ReadingLists");

            migrationBuilder.DropTable(
                name: "ReadingListFolders");

            migrationBuilder.DropTable(
                name: "ReadingListOverlapDismissals");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_ContinuityId",
                table: "ReadingLists");

            migrationBuilder.DropIndex(
                name: "IX_ReadingLists_FolderId",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "ContinuityId",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "ContinuityOrderKind",
                table: "ReadingLists");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "ReadingLists");
        }
    }
}
