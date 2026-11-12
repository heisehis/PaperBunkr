using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>Data only: gives every series whose issues you rated its average rating (<see cref="SeriesRatingBackfill"/>). No schema change.</summary>
    public partial class BackfillSeriesRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeriesRatingBackfill.Run(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the filled ratings are what the app now keeps up to date on its own, and the values they replaced were empty.
        }
    }
}
