using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleDetectionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoleReason",
                table: "ReadingListItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleSource",
                table: "ReadingListItems",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RoleSuggestionDismissed",
                table: "ReadingListItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedReason",
                table: "ReadingListItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedRole",
                table: "ReadingListItems",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleReason",
                table: "EventMemberships",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleSource",
                table: "EventMemberships",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RoleSuggestionDismissed",
                table: "EventMemberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedReason",
                table: "EventMemberships",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedRole",
                table: "EventMemberships",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoleReason",
                table: "ReadingListItems");

            migrationBuilder.DropColumn(
                name: "RoleSource",
                table: "ReadingListItems");

            migrationBuilder.DropColumn(
                name: "RoleSuggestionDismissed",
                table: "ReadingListItems");

            migrationBuilder.DropColumn(
                name: "SuggestedReason",
                table: "ReadingListItems");

            migrationBuilder.DropColumn(
                name: "SuggestedRole",
                table: "ReadingListItems");

            migrationBuilder.DropColumn(
                name: "RoleReason",
                table: "EventMemberships");

            migrationBuilder.DropColumn(
                name: "RoleSource",
                table: "EventMemberships");

            migrationBuilder.DropColumn(
                name: "RoleSuggestionDismissed",
                table: "EventMemberships");

            migrationBuilder.DropColumn(
                name: "SuggestedReason",
                table: "EventMemberships");

            migrationBuilder.DropColumn(
                name: "SuggestedRole",
                table: "EventMemberships");
        }
    }
}
