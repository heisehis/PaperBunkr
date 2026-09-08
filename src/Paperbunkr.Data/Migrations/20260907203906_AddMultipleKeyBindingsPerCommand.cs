using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipleKeyBindingsPerCommand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KeyBindings_CommandId",
                table: "KeyBindings");

            migrationBuilder.CreateIndex(
                name: "IX_KeyBindings_CommandId_Key",
                table: "KeyBindings",
                columns: new[] { "CommandId", "Key" },
                unique: true);
        }

        /// <summary>
        /// Deliberately does not recreate IX_KeyBindings_CommandId (docs/superpowers/specs/2026-09-07-
        /// keyboard-shortcuts-redesign-design.md) - once a command has more than one bound gesture,
        /// restoring a unique index on CommandId alone would fail against real data. Same reasoning
        /// as this project's standing no-op-Down rule for orphaned-column migrations: a rollback that
        /// can fail against legitimate post-migration data is worse than one that does less.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KeyBindings_CommandId_Key",
                table: "KeyBindings");
        }
    }
}
