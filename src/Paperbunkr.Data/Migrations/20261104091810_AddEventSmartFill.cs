using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSmartFill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GcdArcId",
                table: "StoryEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventDateEndpoints",
                columns: table => new
                {
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnd = table.Column<bool>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    Day = table.Column<int>(type: "INTEGER", nullable: true),
                    Uncertain = table.Column<bool>(type: "INTEGER", nullable: false),
                    Provisional = table.Column<bool>(type: "INTEGER", nullable: false),
                    Basis = table.Column<string>(type: "TEXT", nullable: false),
                    Coverage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventDateEndpoints", x => new { x.StoryEventId, x.IsEnd });
                    table.ForeignKey(
                        name: "FK_EventDateEndpoints_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventMetadataChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BeforeValue = table.Column<string>(type: "TEXT", nullable: true),
                    AfterValue = table.Column<string>(type: "TEXT", nullable: true),
                    BeforeProtected = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppliedRevision = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    Undone = table.Column<bool>(type: "INTEGER", nullable: false),
                    MergeInvalidated = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMetadataChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventMetadataChanges_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventMetadataStates",
                columns: table => new
                {
                    StoryEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedBlank = table.Column<bool>(type: "INTEGER", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMetadataStates", x => new { x.StoryEventId, x.Field });
                    table.ForeignKey(
                        name: "FK_EventMetadataStates_StoryEvents_StoryEventId",
                        column: x => x.StoryEventId,
                        principalTable: "StoryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventMetadataChanges_OperationId",
                table: "EventMetadataChanges",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMetadataChanges_StoryEventId_CreatedAt",
                table: "EventMetadataChanges",
                columns: new[] { "StoryEventId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventDateEndpoints");

            migrationBuilder.DropTable(
                name: "EventMetadataChanges");

            migrationBuilder.DropTable(
                name: "EventMetadataStates");

            migrationBuilder.DropColumn(
                name: "GcdArcId",
                table: "StoryEvents");
        }
    }
}
