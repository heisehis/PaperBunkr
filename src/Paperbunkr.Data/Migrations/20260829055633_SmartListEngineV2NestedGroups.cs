using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// SmartList Engine v2 (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2):
    /// replaces the flat always-AND <c>SmartList.Conditions</c> list with a nested AND/OR tree
    /// (<c>SmartListConditionGroups</c>), and adds per-condition <c>Not</c>, <c>IgnoreCase</c> (§3)
    /// and <c>SearchMode</c> (§4, for <c>AllProperties</c> conditions).
    ///
    /// Zero data loss: every existing <c>SmartList</c> gets one new root
    /// <c>SmartListConditionGroup</c> (<c>Mode = And</c>, <c>ParentGroupId = null</c>), and every
    /// existing <c>SmartListCondition</c> is repointed at its list's new root group with
    /// <c>Not = false</c> / <c>IgnoreCase = true</c> — exactly the flat-AND, case-insensitive
    /// semantics every existing list already has, so no list changes behaviour on upgrade. The old
    /// <c>SmartListConditions.SmartListId</c> column is kept through the backfill, then dropped.
    /// </summary>
    public partial class SmartListEngineV2NestedGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartListConditionGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SmartListId = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentGroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartListConditionGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartListConditionGroups_SmartListConditionGroups_ParentGroupId",
                        column: x => x.ParentGroupId,
                        principalTable: "SmartListConditionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SmartListConditionGroups_SmartLists_SmartListId",
                        column: x => x.SmartListId,
                        principalTable: "SmartLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // New per-condition columns. GroupId starts 0 (backfilled below); Not defaults false and
            // IgnoreCase defaults true, matching CE's own defaults and every existing condition's
            // current de-facto behaviour.
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "SmartListConditions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Not",
                table: "SmartListConditions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreCase",
                table: "SmartListConditions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchMode",
                table: "SmartListConditions",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            // One root group per existing list.
            migrationBuilder.Sql(@"
                INSERT INTO ""SmartListConditionGroups"" (""SmartListId"", ""ParentGroupId"", ""Mode"", ""SortOrder"")
                SELECT ""Id"", NULL, 'And', 0 FROM ""SmartLists"";
            ");

            // Repoint every existing condition at its list's new root group. (Not/IgnoreCase already
            // hold their column defaults — false / true — from the AddColumn calls above.)
            migrationBuilder.Sql(@"
                UPDATE ""SmartListConditions""
                SET ""GroupId"" = (
                    SELECT g.""Id"" FROM ""SmartListConditionGroups"" g
                    WHERE g.""SmartListId"" = ""SmartListConditions"".""SmartListId""
                );
            ");

            // The old direct SmartList FK is now redundant — a condition reaches its list through
            // its group. Drop it (SQLite rebuilds the table).
            migrationBuilder.DropForeignKey(
                name: "FK_SmartListConditions_SmartLists_SmartListId",
                table: "SmartListConditions");

            migrationBuilder.DropIndex(
                name: "IX_SmartListConditions_SmartListId",
                table: "SmartListConditions");

            migrationBuilder.DropColumn(
                name: "SmartListId",
                table: "SmartListConditions");

            migrationBuilder.CreateIndex(
                name: "IX_SmartListConditions_GroupId",
                table: "SmartListConditions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartListConditionGroups_ParentGroupId",
                table: "SmartListConditionGroups",
                column: "ParentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartListConditionGroups_SmartListId",
                table: "SmartListConditionGroups",
                column: "SmartListId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SmartListConditions_SmartListConditionGroups_GroupId",
                table: "SmartListConditions",
                column: "GroupId",
                principalTable: "SmartListConditionGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SmartListId",
                table: "SmartListConditions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Fold the tree back to flat: a condition's list is its (root or nested) group's
            // owning list. Only the pre-v2 shape (one flat root group per list) round-trips
            // losslessly; a genuinely nested list collapses to a flat AND of every leaf condition.
            migrationBuilder.Sql(@"
                WITH RECURSIVE group_list(GroupId, SmartListId) AS (
                    SELECT ""Id"", ""SmartListId"" FROM ""SmartListConditionGroups"" WHERE ""SmartListId"" IS NOT NULL
                    UNION ALL
                    SELECT g.""Id"", gl.SmartListId
                    FROM ""SmartListConditionGroups"" g
                    JOIN group_list gl ON g.""ParentGroupId"" = gl.GroupId
                )
                UPDATE ""SmartListConditions""
                SET ""SmartListId"" = (
                    SELECT gl.SmartListId FROM group_list gl WHERE gl.GroupId = ""SmartListConditions"".""GroupId""
                );
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_SmartListConditions_SmartListConditionGroups_GroupId",
                table: "SmartListConditions");

            migrationBuilder.DropTable(
                name: "SmartListConditionGroups");

            migrationBuilder.DropIndex(
                name: "IX_SmartListConditions_GroupId",
                table: "SmartListConditions");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "SmartListConditions");

            migrationBuilder.DropColumn(
                name: "Not",
                table: "SmartListConditions");

            migrationBuilder.DropColumn(
                name: "IgnoreCase",
                table: "SmartListConditions");

            migrationBuilder.DropColumn(
                name: "SearchMode",
                table: "SmartListConditions");

            migrationBuilder.CreateIndex(
                name: "IX_SmartListConditions_SmartListId",
                table: "SmartListConditions",
                column: "SmartListId");

            migrationBuilder.AddForeignKey(
                name: "FK_SmartListConditions_SmartLists_SmartListId",
                table: "SmartListConditions",
                column: "SmartListId",
                principalTable: "SmartLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
