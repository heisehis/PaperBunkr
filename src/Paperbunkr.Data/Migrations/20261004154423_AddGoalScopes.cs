using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DistinctOnly",
                table: "ReadingGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ReadingGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReadingGoalScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingGoalId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Label = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingGoalScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingGoalScopes_ReadingGoals_ReadingGoalId",
                        column: x => x.ReadingGoalId,
                        principalTable: "ReadingGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingGoalScopes_ReadingGoalId",
                table: "ReadingGoalScopes",
                column: "ReadingGoalId");

            // A goal's single legacy scope (ScopeKind: Series=1 / Publisher=2 / Genre=3) becomes one scope row, so existing goals keep
            // their meaning under the combined-scope model. The legacy columns stay as they are (see ReadingGoal.ScopeKind).
            migrationBuilder.Sql(
                "INSERT INTO ReadingGoalScopes (ReadingGoalId, Kind, Value, Label) " +
                "SELECT Id, ScopeKind, ScopeValue, ScopeValue FROM ReadingGoals WHERE ScopeKind <> 0 AND ScopeValue IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drops the table only. The scaffolded DropColumn of ReadingGoals.Kind / DistinctOnly is deliberately NOT run: a DropColumn makes
            // SQLite rebuild the table, which silently drops columns the model no longer knows about (the project's orphan-column rule -
            // see AddNavRailHoverExpandEnabled.Down). The two columns are harmless left behind.
            migrationBuilder.DropTable(
                name: "ReadingGoalScopes");
        }
    }
}
