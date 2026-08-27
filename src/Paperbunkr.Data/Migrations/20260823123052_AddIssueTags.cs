using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited from the scaffolded migration (docs/superpowers/specs/2026-08-23-weighted-
            // categorized-tags-design.md), same reordering fix as MetadataModelPhase1CanonicalMetadata:
            // the scaffolded version dropped Genre/Tags before creating IssueTags, which would destroy
            // the source data before the backfill below could read it. CreateTable/backfill/Drop
            // instead, in an order that reads the old columns before they're removed.

            migrationBuilder.CreateTable(
                name: "IssueTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueTags_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueTags_IssueId",
                table: "IssueTags",
                column: "IssueId");

            // Backfill from the outgoing Genre/Tags CSV columns before they're dropped below. SQLite
            // has no built-in string-split function, so this uses a recursive CTE to walk each
            // comma-separated value out into its own row - verified against edge cases (multi-value,
            // leading/trailing whitespace, embedded quotes, null, empty string, all-commas,
            // trailing comma, no-comma-single-value) in an isolated in-memory SQLite database before
            // being used here. Weight always starts Unset (never inferred); Category defaults to
            // "Genre" for the Genre field, "Uncategorized" for the Tags field, matching the same
            // migration-default rule the design spec applies everywhere else new tags are created
            // (e.g. import diff, bulk-edit diff).
            migrationBuilder.Sql(
                """
                WITH RECURSIVE split(IssueId, Field, rest, Value) AS (
                  SELECT Id, 'Genre', Genre || ',', '' FROM Issues WHERE Genre IS NOT NULL AND trim(Genre) != ''
                  UNION ALL
                  SELECT Id, 'Tags', Tags || ',', '' FROM Issues WHERE Tags IS NOT NULL AND trim(Tags) != ''
                  UNION ALL
                  SELECT IssueId, Field, substr(rest, instr(rest, ',') + 1), substr(rest, 1, instr(rest, ',') - 1)
                  FROM split WHERE rest != ''
                )
                INSERT INTO IssueTags (IssueId, Field, Value, Category, Weight)
                SELECT IssueId, Field, trim(Value),
                       CASE Field WHEN 'Genre' THEN 'Genre' ELSE 'Uncategorized' END,
                       'Unset'
                FROM split WHERE trim(Value) != '';
                """);

            migrationBuilder.DropColumn(
                name: "Genre",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Issues");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Genre",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            // Best-effort restore, not a perfect inverse: weight/category info is lost (the old
            // columns never had it), and per-issue tag order isn't preserved (Id order within each
            // Issue/Field is the closest available proxy for original comma-list order).
            migrationBuilder.Sql(
                """
                UPDATE Issues SET Genre = (
                    SELECT group_concat(Value, ', ') FROM (
                        SELECT Value FROM IssueTags WHERE IssueId = Issues.Id AND Field = 'Genre' ORDER BY Id
                    )
                );
                """);

            migrationBuilder.Sql(
                """
                UPDATE Issues SET Tags = (
                    SELECT group_concat(Value, ', ') FROM (
                        SELECT Value FROM IssueTags WHERE IssueId = Issues.Id AND Field = 'Tags' ORDER BY Id
                    )
                );
                """);

            migrationBuilder.DropTable(
                name: "IssueTags");
        }
    }
}
