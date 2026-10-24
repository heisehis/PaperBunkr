using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratedReadingListTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeneratedKey",
                table: "ReadingListTags",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ReadingListTags",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListTags_ReadingListId_GeneratedKey",
                table: "ReadingListTags",
                columns: new[] { "ReadingListId", "GeneratedKey" },
                unique: true,
                filter: "\"GeneratedKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadingListTags_ReadingListId_GeneratedKey",
                table: "ReadingListTags");

            migrationBuilder.DropColumn(
                name: "GeneratedKey",
                table: "ReadingListTags");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ReadingListTags");
        }
    }
}
