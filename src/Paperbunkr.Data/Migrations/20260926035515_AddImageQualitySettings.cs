using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImageQualitySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoLevelsOverride",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SharpenOverride",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoCropMargins",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultAutoLevels",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSharpen",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PageCropOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageCropOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageCropOverrides_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageCropOverrides_IssueId_PageNumber",
                table: "PageCropOverrides",
                columns: new[] { "IssueId", "PageNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md): DropColumn rebuilds AppSettings and Issues, and the rollback chain has orphaned columns before; an unused column or table left behind is harmless.
        }
    }
}
