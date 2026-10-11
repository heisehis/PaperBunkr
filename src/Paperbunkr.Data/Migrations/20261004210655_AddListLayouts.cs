using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListLayoutAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Screen = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SelectionKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListLayoutAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListLayouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Screen = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListLayouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListLayoutAssignments_Screen_SelectionKey",
                table: "ListLayoutAssignments",
                columns: new[] { "Screen", "SelectionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListLayouts_Screen_Name",
                table: "ListLayouts",
                columns: new[] { "Screen", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListLayoutAssignments");

            migrationBuilder.DropTable(
                name: "ListLayouts");
        }
    }
}
