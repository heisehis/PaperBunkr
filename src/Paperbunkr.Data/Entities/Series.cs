namespace Paperbunkr.Data.Entities;

/// <summary>
/// A comic/manga series — new first-class entity (docs/onboarding.md §6). CE has no equivalent:
/// its <c>ComicBook</c>/<c>ComicInfo</c> only carries a flat <c>Series</c> string field, with
/// series-level facts (like <c>SeriesComplete</c>) duplicated onto every issue for lack of
/// anywhere else to put them. Elevating Series to a real entity fixes that, matching how Mihon
/// separates Manga from Chapter.
/// </summary>
public class Series
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public ContentType ContentType { get; set; } = ContentType.Unknown;

    public ReadingMode ReadingMode { get; set; } = ReadingMode.LeftToRight;

    /// <summary>Series-level completion flag. Replaces CE's per-issue <c>SeriesComplete</c>.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Populated once at CE-migration time. Not the current source of truth for filtering/display — see <see cref="Issue.Publisher"/>.</summary>
    public string? Publisher { get; set; }

    /// <summary>Populated once at CE-migration time. Not the current source of truth for filtering/display — see <see cref="Issue.Genre"/>.</summary>
    public string? Genre { get; set; }

    public string? Summary { get; set; }

    /// <summary>Issue whose cover thumbnail represents the series (e.g. in library grid views).</summary>
    public int? CoverIssueId { get; set; }

    public Issue? CoverIssue { get; set; }

    public List<Issue> Issues { get; set; } = new();

    public List<Category> Categories { get; set; } = new();

    public List<TrackingLink> TrackingLinks { get; set; } = new();
}
