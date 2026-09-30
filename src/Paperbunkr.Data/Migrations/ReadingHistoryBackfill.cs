using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Migrations;

/// <summary>
/// One-time fill of the <c>ReadingEvents.SeriesTitle</c> / <c>ItemLabel</c> name snapshots for rows written
/// before the Insights History tab existed (docs/superpowers/specs/2026-09-29-insights-reading-history-
/// design.md §1). Invoked from the <c>AddReadingHistoryColumns</c> migration's <c>Up</c>; kept as its own type
/// so the exact SQL can be unit-tested, same shape as <see cref="ReadingEventBackfill"/>.
///
/// Only rows whose item still exists get names - a row whose item was deleted before this ran stays null and
/// the History resolver skips it (design Q16). Raw SQL runs without EF query filters, so remote-library rows
/// are covered too. The comic label mirrors <see cref="Metadata.ReadingHistoryLabels.IssueLabel"/>'s
/// precedence (<c>#Number</c>, else <c>Vol. Volume</c>, else <c>Title</c>) over the plain columns only - an
/// issue whose number exists only as an accepted metadata proposal gets its label from the next precedence
/// step instead. Cosmetic, and only for pre-existing rows. Idempotent: re-running rewrites the same values.
/// </summary>
public static class ReadingHistoryBackfill
{
    public static readonly string[] Statements =
    {
        // Comics/manga: series name + #Number / Vol. Volume / Title.
        """
        UPDATE "ReadingEvents"
        SET "SeriesTitle" = (SELECT s."Name" FROM "Issues" i JOIN "Series" s ON s."Id" = i."SeriesId" WHERE i."Id" = "ReadingEvents"."ItemId"),
            "ItemLabel" = (SELECT CASE
                                      WHEN NULLIF(TRIM(i."Number"), '') IS NOT NULL THEN '#' || TRIM(i."Number")
                                      WHEN NULLIF(TRIM(i."Volume"), '') IS NOT NULL THEN 'Vol. ' || TRIM(i."Volume")
                                      ELSE NULLIF(TRIM(i."Title"), '')
                                  END
                           FROM "Issues" i WHERE i."Id" = "ReadingEvents"."ItemId")
        WHERE "ItemType" = 'Comic'
          AND EXISTS (SELECT 1 FROM "Issues" i JOIN "Series" s ON s."Id" = i."SeriesId" WHERE i."Id" = "ReadingEvents"."ItemId");
        """,

        // Books in a book series: series name + book title.
        """
        UPDATE "ReadingEvents"
        SET "SeriesTitle" = (SELECT COALESCE(NULLIF(TRIM(bs."Name"), ''), b."Title") FROM "Books" b JOIN "BookSeries" bs ON bs."Id" = b."BookSeriesId" WHERE b."Id" = "ReadingEvents"."ItemId"),
            "ItemLabel" = (SELECT NULLIF(TRIM(b."Title"), '') FROM "Books" b WHERE b."Id" = "ReadingEvents"."ItemId")
        WHERE "ItemType" = 'Novel'
          AND EXISTS (SELECT 1 FROM "Books" b JOIN "BookSeries" bs ON bs."Id" = b."BookSeriesId" WHERE b."Id" = "ReadingEvents"."ItemId");
        """,

        // Standalone books: the title is the group name, no per-item label.
        """
        UPDATE "ReadingEvents"
        SET "SeriesTitle" = (SELECT b."Title" FROM "Books" b WHERE b."Id" = "ReadingEvents"."ItemId"),
            "ItemLabel" = NULL
        WHERE "ItemType" = 'Novel'
          AND EXISTS (SELECT 1 FROM "Books" b WHERE b."Id" = "ReadingEvents"."ItemId" AND b."BookSeriesId" IS NULL);
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
