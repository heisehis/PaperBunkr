using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventOrderOverlapNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceYear",
                table: "EventOrderEvidence",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventOrderOverlapNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventOrderEvidenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    TargetYear = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventOrderOverlapNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventOrderOverlapNotes_EventOrderEvidence_EventOrderEvidenceId",
                        column: x => x.EventOrderEvidenceId,
                        principalTable: "EventOrderEvidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventOrderOverlapNotes_EventOrderEvidenceId",
                table: "EventOrderOverlapNotes",
                column: "EventOrderEvidenceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventOrderOverlapNotes");

            migrationBuilder.DropColumn(
                name: "SourceYear",
                table: "EventOrderEvidence");
        }
    }
}
