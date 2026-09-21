namespace Paperbunkr.Data.Entities;

/// <summary>
/// One issue from the weekly pull list (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md): a release Metron lists with a store date in the window the daemon
/// fetches (last week to a few weeks ahead). Cached so the Releases tab opens instantly and works offline. A row is about the release itself and belongs to no watched
/// series; whether a followed series claims it is worked out when it is shown or promoted.
/// </summary>
public class PullListRelease
{
    public int Id { get; set; }

    /// <summary>Which source listed this release. The list comes from one source at a time (Metron when its login is saved, else ComicVine); rows saved before ComicVine was an option are Metron's.</summary>
    public ComicProvider Provider { get; set; } = ComicProvider.Metron;

    /// <summary>The source's issue id.</summary>
    public int ExternalIssueId { get; set; }

    /// <summary>The source's series (ComicVine: volume) id for the release.</summary>
    public int SeriesId { get; set; }

    public string SeriesName { get; set; } = string.Empty;

    public string IssueNumber { get; set; } = string.Empty;

    public DateTime StoreDate { get; set; }

    public DateTime? CoverDate { get; set; }

    public string? CoverImageUrl { get; set; }

    public DateTime FetchedAt { get; set; }

    /// <summary>The user hid this release (not interested). Kept across refreshes, and a hidden release is never turned into a want.</summary>
    public bool IsHidden { get; set; }
}

/// <summary>
/// What the list's source says about a series, cached because a release list item doesn't carry it: the publisher (for the Releases filter) and, for Metron, the ComicVine id
/// (to recognise a release that belongs to a series tracked on ComicVine). Keyed by the source and its series id.
/// </summary>
public class ReleaseSeriesInfo
{
    public ComicProvider Provider { get; set; } = ComicProvider.Metron;

    public int SeriesId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Publisher { get; set; }

    public int? YearBegan { get; set; }

    /// <summary>The same series' ComicVine volume id, when Metron knows it.</summary>
    public int? ComicVineId { get; set; }

    public DateTime FetchedAt { get; set; }
}
