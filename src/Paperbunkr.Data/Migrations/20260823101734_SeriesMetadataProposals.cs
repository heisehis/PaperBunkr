using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeriesMetadataProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "IssueId",
                table: "MetadataProposals",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "ProviderKey",
                table: "MetadataProposals",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "MetadataProposals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProposals_SeriesId",
                table: "MetadataProposals",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataProposals_Series_SeriesId",
                table: "MetadataProposals",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MetadataProposals_Series_SeriesId",
                table: "MetadataProposals");

            migrationBuilder.DropIndex(
                name: "IX_MetadataProposals_SeriesId",
                table: "MetadataProposals");

            migrationBuilder.DropColumn(
                name: "ProviderKey",
                table: "MetadataProposals");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "MetadataProposals");

            migrationBuilder.AlterColumn<int>(
                name: "IssueId",
                table: "MetadataProposals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
