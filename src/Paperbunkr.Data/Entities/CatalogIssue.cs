namespace Paperbunkr.Data.Entities;

/// <summary>
/// A cached row of a watched volume's ComicVine issue list. Kept so the series "Missing Issues" section
/// and the Upcoming list don't spend ComicVine's 200-requests-per-hour budget on every page view; the
/// daemon refreshes it (low priority). Missing = catalog issues that are neither owned nor already wanted.
/// </summary>
public class CatalogIssue
{
    public int Id { get; set; }

    public int WatchedSeriesId { get; set; }

    public WatchedSeries? WatchedSeries { get; set; }

    public int ComicVineIssueId { get; set; }

    /// <summary>Issue number as text ("5", "0", "1.5", "Annual 1") - never assumed numeric.</summary>
    public string IssueNumber { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTime? StoreDate { get; set; }

    public DateTime? CoverDate { get; set; }

    public string? CoverImageUrl { get; set; }
}
