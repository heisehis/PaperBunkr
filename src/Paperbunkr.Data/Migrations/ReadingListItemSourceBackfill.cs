using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Migrations;

/// <summary>
/// One-time fill of <c>ReadingListItemSources</c> for the reading-list entries that were waiting on a placeholder before source records
/// existed (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §4, decision Q11). Invoked from the
/// <c>AddReadingListItemSource</c> migration's <c>Up</c>; its own type so the SQL can be unit-tested, same shape as
/// <see cref="ReadingHistoryBackfill"/>.
///
/// A placeholder still holds what the source asked for (its series row's name, number, volume, year and format), so that is what the
/// record gets. An entry linked to a real comic gets none: what it was asked for is no longer known, and nothing is invented. No
/// provider id is written. The two comparison keys are left empty here because they are computed in C#
/// (<see cref="ReadingLists.Matching.ReadingListSourceKeys.FillMissing"/> fills them the first time they are needed). Idempotent.
/// </summary>
public static class ReadingListItemSourceBackfill
{
    public static readonly string[] Statements =
    {
        """
        INSERT INTO "ReadingListItemSources"
            ("ReadingListItemId", "Series", "Number", "Volume", "Year", "Format", "SeriesKey", "NumberKey", "SourceKey", "Method", "State")
        SELECT li."Id", s."Name", COALESCE(i."Number", ''), i."Volume", i."Year", i."Format", '', '', rl."Source", 'Backfilled', 'Unresolved'
        FROM "ReadingListItems" li
        JOIN "Issues" i ON i."Id" = li."IssueId"
        JOIN "Series" s ON s."Id" = i."SeriesId"
        JOIN "ReadingLists" rl ON rl."Id" = li."ReadingListId"
        WHERE i."IsPlaceholder" = 1
          AND NOT EXISTS (SELECT 1 FROM "ReadingListItemSources" x WHERE x."ReadingListItemId" = li."Id");
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
