using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BooksActiveWorkspaceId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibraryActiveWorkspaceId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Screen = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Screen_SortOrder",
                table: "Workspaces",
                columns: new[] { "Screen", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropColumn(
                name: "BooksActiveWorkspaceId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LibraryActiveWorkspaceId",
                table: "AppSettings");
        }
    }
}
