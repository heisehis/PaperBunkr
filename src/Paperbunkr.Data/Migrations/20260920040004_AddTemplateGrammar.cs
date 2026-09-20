using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateGrammar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RenameTemplateGrammar",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RenameTemplateOriginal",
                table: "AcquisitionSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RenameTemplateUpgradeFailed",
                table: "AcquisitionSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenameTemplateGrammar",
                table: "AcquisitionSettings");

            migrationBuilder.DropColumn(
                name: "RenameTemplateOriginal",
                table: "AcquisitionSettings");

            migrationBuilder.DropColumn(
                name: "RenameTemplateUpgradeFailed",
                table: "AcquisitionSettings");
        }
    }
}
