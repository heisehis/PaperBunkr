using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReaderProfileId",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultReaderProfileId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md): DropColumn rebuilds AppSettings and the rollback chain has orphaned columns before; an unused column left behind is harmless.
        }
    }
}
