using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlackAndWhite",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BookAge",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookCollectionStatus",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookCondition",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookLocation",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookNotes",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookOwner",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "BookPrice",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookStore",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Checked",
                table: "Issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<float>(
                name: "CommunityRating",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FileCreationTime",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FileModifiedTime",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ISBN",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MainCharacterOrTeam",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewPages",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "Rating",
                table: "Issues",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanInformation",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IssueCustomValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueCustomValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueCustomValues_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmartLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartListConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SmartListId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Value2 = table.Column<string>(type: "TEXT", nullable: true),
                    CustomValueName = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartListConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartListConditions_SmartLists_SmartListId",
                        column: x => x.SmartListId,
                        principalTable: "SmartLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueCustomValues_IssueId_Name",
                table: "IssueCustomValues",
                columns: new[] { "IssueId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartListConditions_SmartListId",
                table: "SmartListConditions",
                column: "SmartListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueCustomValues");

            migrationBuilder.DropTable(
                name: "SmartListConditions");

            migrationBuilder.DropTable(
                name: "SmartLists");

            migrationBuilder.DropColumn(
                name: "BlackAndWhite",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookAge",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookCollectionStatus",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookCondition",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookLocation",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookNotes",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookOwner",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookPrice",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BookStore",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Checked",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "CommunityRating",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "FileCreationTime",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "FileModifiedTime",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ISBN",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "MainCharacterOrTeam",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "NewPages",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ScanInformation",
                table: "Issues");
        }
    }
}
