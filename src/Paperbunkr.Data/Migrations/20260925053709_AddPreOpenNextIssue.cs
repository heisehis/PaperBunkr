using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreOpenNextIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PreOpenNextIssue",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md A): DropColumn rebuilds AppSettings and the
            // rollback chain has orphaned columns before; an unused column left behind is harmless.
        }
    }
}
