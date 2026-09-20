using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProfileName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizeBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FolderTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    FileTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    AutomationCollisionPolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoveEmptyFolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseForScheduledRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    BaseFolder = table.Column<string>(type: "TEXT", nullable: false),
                    ExcludeRuleJson = table.Column<string>(type: "TEXT", nullable: true),
                    MonthNamesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizerProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizeMoves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    OldPath = table.Column<string>(type: "TEXT", nullable: false),
                    NewPath = table.Column<string>(type: "TEXT", nullable: false),
                    MovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsReverted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizeMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizeMoves_OrganizeBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "OrganizeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizeMoves_BatchId",
                table: "OrganizeMoves",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizeMoves");

            migrationBuilder.DropTable(
                name: "OrganizerProfiles");

            migrationBuilder.DropTable(
                name: "OrganizeBatches");
        }
    }
}
