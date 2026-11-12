using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Migrations;

/// <summary>
/// One-time fill of <c>Series.Rating</c> from the issues' ratings. Until 2026-10-31 nothing called
/// <see cref="Metadata.SeriesRatingResolver"/>, so rating issues never gave their series a rating and Insights' "You vs Community"
/// and its fetch action saw almost no rated series. Invoked from the <c>BackfillSeriesRating</c> migration's <c>Up</c>; its own type so
/// the SQL can be unit-tested, same shape as <see cref="ReadingListItemSourceBackfill"/>.
///
/// Only fills series with no rating yet: a score pulled in with a tracker's "Use this score" is kept until an issue rating next changes.
/// Rated means above 0, as in the resolver. Idempotent.
/// </summary>
public static class SeriesRatingBackfill
{
    public static readonly string[] Statements =
    {
        """
        UPDATE "Series"
        SET "Rating" = (SELECT AVG(i."Rating") FROM "Issues" i WHERE i."SeriesId" = "Series"."Id" AND i."Rating" > 0)
        WHERE "Rating" IS NULL
          AND EXISTS (SELECT 1 FROM "Issues" i WHERE i."SeriesId" = "Series"."Id" AND i."Rating" > 0);
        """,
    };

    public static void Run(MigrationBuilder migrationBuilder)
    {
        foreach (var sql in Statements)
        {
            migrationBuilder.Sql(sql);
        }
    }
}
