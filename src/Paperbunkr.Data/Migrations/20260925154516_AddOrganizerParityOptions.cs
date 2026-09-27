using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizerParityOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmptyDataJson",
                table: "OrganizerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmptyFolder",
                table: "OrganizerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExcludeFoldersJson",
                table: "OrganizerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "FailEmptyValues",
                table: "OrganizerProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FailedFieldsJson",
                table: "OrganizerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "UseFileName",
                table: "OrganizerProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseFolder",
                table: "OrganizerProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseFolder",
                table: "OrganizeBatches",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmptyDataJson",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "EmptyFolder",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "ExcludeFoldersJson",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "FailEmptyValues",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "FailedFieldsJson",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "UseFileName",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "UseFolder",
                table: "OrganizerProfiles");

            migrationBuilder.DropColumn(
                name: "BaseFolder",
                table: "OrganizeBatches");
        }
    }
}
