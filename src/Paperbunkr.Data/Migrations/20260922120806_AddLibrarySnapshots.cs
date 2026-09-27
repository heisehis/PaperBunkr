using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibrarySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibrarySnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalOwnedComics = table.Column<int>(type: "INTEGER", nullable: false),
                    BacklogComics = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibrarySnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySnapshots_SnapshotDate",
                table: "LibrarySnapshots",
                column: "SnapshotDate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibrarySnapshots");
        }
    }
}
