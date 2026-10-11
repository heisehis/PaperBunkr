using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWriteMetronInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WriteMetronInfo",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op, like 20260903211057_AddMetadataWriteBackSettings (which explains
            // why): a DropColumn on AppSettings rebuilds the table from the previous snapshot and
            // takes the columns earlier migrations orphaned with it.
        }
    }
}
