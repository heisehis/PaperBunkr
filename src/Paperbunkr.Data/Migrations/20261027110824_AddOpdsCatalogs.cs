using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpdsCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpdsDownloadFolder",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpdsCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    AddedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpdsCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpdsDownloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogId = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryId = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BookId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpdsDownloads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpdsDownloads_CatalogId_EntryId",
                table: "OpdsDownloads",
                columns: new[] { "CatalogId", "EntryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpdsCatalogs");

            migrationBuilder.DropTable(
                name: "OpdsDownloads");

            migrationBuilder.DropColumn(
                name: "OpdsDownloadFolder",
                table: "AppSettings");
        }
    }
}
