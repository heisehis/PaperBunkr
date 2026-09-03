using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Adds <c>Issue.CoverAspectRatio</c> (nullable) - cover width/height, so Panorama grid can
    /// render each cover at its true shape while virtualized without eagerly decoding every cover
    /// to measure it. Plain additive column; existing rows stay null and fall back to a default
    /// portrait ratio until a cover is generated or seen on screen.
    /// </summary>
    public partial class AddCoverAspectRatio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CoverAspectRatio",
                table: "Issues",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverAspectRatio",
                table: "Issues");
        }
    }
}
