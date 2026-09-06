using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Migrations;

/// <summary>
/// One-time backfill of the <c>ReadingEvents</c> log from the point-in-time read state that predates
/// it (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md §4.1). Invoked from the
/// <c>AddReadingEventLog</c> migration's <c>Up</c>; kept as its own type so the exact SQL can be
/// unit-tested against a migrated database with the events table cleared, rather than only through a
/// full migration run.
///
/// <c>ItemType</c>/<c>Kind</c> are written as their enum *string* names to match the context's
/// <c>HasConversion&lt;string&gt;()</c> storage. <c>PagesRead</c> and <c>PrimaryGenre</c> stay NULL
/// (unknowable for historical rows). The 0.95 threshold mirrors
/// <c>IssueMetadataExtensions.ReadThresholdPercent</c> (95) - keep in sync if that ever changes.
/// </summary>
public static class ReadingEventBackfill
{
    public static readonly string[] Statements =
    {
        // Comics/manga: one Opened per issue ever opened.
        """
        INSERT INTO "ReadingEvents" ("ItemType", "ItemId", "Kind", "TimestampUtc", "PagesRead", "SeriesId", "Publisher", "PrimaryGenre")
        SELECT 'Comic', "Id", 'Opened', "OpenedTime", NULL, "SeriesId", "Publisher", NULL
        FROM "Issues"
        WHERE "OpenedTime" IS NOT NULL;
        """,

        // Comics/manga: a Finished for every issue read through to >= 95%.
        """
        INSERT INTO "ReadingEvents" ("ItemType", "ItemId", "Kind", "TimestampUtc", "PagesRead", "SeriesId", "Publisher", "PrimaryGenre")
        SELECT 'Comic', "Id", 'Finished', "OpenedTime", NULL, "SeriesId", "Publisher", NULL
        FROM "Issues"
        WHERE "OpenedTime" IS NOT NULL
          AND "PageCount" IS NOT NULL AND "PageCount" > 0
          AND (CAST(COALESCE("LastPageRead", 0) AS REAL) / "PageCount") >= 0.95;
        """,

        // Novels: one Opened per book ever opened.
        """
        INSERT INTO "ReadingEvents" ("ItemType", "ItemId", "Kind", "TimestampUtc", "PagesRead", "SeriesId", "Publisher", "PrimaryGenre")
        SELECT 'Novel', "Id", 'Opened', "LastOpenedTime", NULL, "BookSeriesId", NULL, NULL
        FROM "Books"
        WHERE "LastOpenedTime" IS NOT NULL;
        """,

        // Novels: a Finished for every book flagged finished.
        """
        INSERT INTO "ReadingEvents" ("ItemType", "ItemId", "Kind", "TimestampUtc", "PagesRead", "SeriesId", "Publisher", "PrimaryGenre")
        SELECT 'Novel', "Id", 'Finished', "LastOpenedTime", NULL, "BookSeriesId", NULL, NULL
        FROM "Books"
        WHERE "LastOpenedTime" IS NOT NULL AND "Finished" = 1;
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
