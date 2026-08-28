using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Replaces the implicit <c>ContinuitySeries</c> skip-navigation join with the explicit
    /// <c>ContinuityMemberships</c> entity (docs/superpowers/specs/2026-08-28-continuity-editing-
    /// design.md, Part C) so a membership can carry a <c>Note</c> and a deliberate <c>SortOrder</c>.
    /// Existing join rows are copied across before the old table is dropped: <c>Note</c> starts
    /// null, <c>SortOrder</c> is the per-continuity row number ordered by series name so the member
    /// grid keeps its current (alphabetical) order until the user reorders it.
    /// </summary>
    public partial class ContinuityMembershipJoinEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContinuityMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContinuityId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuityMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContinuityMemberships_Continuities_ContinuityId",
                        column: x => x.ContinuityId,
                        principalTable: "Continuities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContinuityMemberships_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContinuityMemberships_ContinuityId_SeriesId",
                table: "ContinuityMemberships",
                columns: new[] { "ContinuityId", "SeriesId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContinuityMemberships_SeriesId",
                table: "ContinuityMemberships",
                column: "SeriesId");

            // Copy the existing implicit-join rows across. SortOrder = 0-based row number within
            // each continuity, ordered by series name (SQLite bundled with Microsoft.Data.Sqlite is
            // well past 3.25, so ROW_NUMBER() is available).
            migrationBuilder.Sql(@"
                INSERT INTO ""ContinuityMemberships"" (""ContinuityId"", ""SeriesId"", ""Note"", ""SortOrder"")
                SELECT cs.""ContinuitiesId"", cs.""SeriesId"", NULL,
                       ROW_NUMBER() OVER (PARTITION BY cs.""ContinuitiesId"" ORDER BY s.""Name"", cs.""SeriesId"") - 1
                FROM ""ContinuitySeries"" cs
                JOIN ""Series"" s ON s.""Id"" = cs.""SeriesId"";
            ");

            migrationBuilder.DropTable(
                name: "ContinuitySeries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContinuitySeries",
                columns: table => new
                {
                    ContinuitiesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuitySeries", x => new { x.ContinuitiesId, x.SeriesId });
                    table.ForeignKey(
                        name: "FK_ContinuitySeries_Continuities_ContinuitiesId",
                        column: x => x.ContinuitiesId,
                        principalTable: "Continuities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContinuitySeries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContinuitySeries_SeriesId",
                table: "ContinuitySeries",
                column: "SeriesId");

            // Fold the note/sort-order back out - the implicit join can't hold them.
            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""ContinuitySeries"" (""ContinuitiesId"", ""SeriesId"")
                SELECT ""ContinuityId"", ""SeriesId"" FROM ""ContinuityMemberships"";
            ");

            migrationBuilder.DropTable(
                name: "ContinuityMemberships");
        }
    }
}
