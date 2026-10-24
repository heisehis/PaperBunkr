namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>
/// Completes the source records the migration's backfill wrote (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §4): SQL
/// cannot compute <see cref="SeriesKeys"/> or <see cref="IssueNumberKey"/>, so those rows arrive with empty keys and are finished here,
/// the first time anything needs them. After that the query finds nothing and costs one indexed lookup.
/// </summary>
public static class ReadingListSourceKeys
{
    /// <summary>Returns how many records were completed. Saves when there were any.</summary>
    public static int FillMissing(PaperbunkrDbContext context)
    {
        var incomplete = context.ReadingListItemSources.Where(s => s.SeriesKey == "").ToList();
        foreach (var source in incomplete)
        {
            source.SeriesKey = SeriesKeys.Of(source.Series);
            source.NumberKey = IssueNumberKey.Of(source.Number);
        }

        if (incomplete.Count > 0)
        {
            context.SaveChanges();
        }

        return incomplete.Count;
    }
}
