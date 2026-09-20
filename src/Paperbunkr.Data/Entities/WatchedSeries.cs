namespace Paperbunkr.Data.Entities;

/// <summary>
/// A ComicVine volume Paperbunkr tracks for acquisition (Mylar's "series"). It is the link between the
/// ComicVine world and the local library: <see cref="SeriesId"/> is the local <see cref="Series"/> it
/// maps to, since neither <c>Series</c> nor <c>Issue</c> carries a ComicVine id
/// (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §4).
/// </summary>
public class WatchedSeries
{
    public int Id { get; set; }

    /// <summary>ComicVine volume id (the number after <c>4050-</c>). Unique.</summary>
    public int ComicVineVolumeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Publisher { get; set; }

    public int? StartYear { get; set; }

    public string? CoverImageUrl { get; set; }

    /// <summary>Local series this volume maps to; <c>null</c> until one exists. Cleared (not cascaded) if the series is deleted.</summary>
    public int? SeriesId { get; set; }

    public Series? Series { get; set; }

    /// <summary>
    /// "Watch series": when true the daemon requests each future issue of the volume automatically.
    /// False means tracked only for naming/folder purposes (e.g. created by an arc request for one
    /// crossover issue) - it must never silently subscribe the user to a whole series.
    /// </summary>
    public bool WatchFutureReleases { get; set; }

    public bool IsPaused { get; set; }

    public DateTime AddedAt { get; set; }

    public DateTime? LastRefreshedAt { get; set; }

    public List<CatalogIssue> Catalog { get; set; } = new();

    public List<WantedIssue> WantedIssues { get; set; } = new();
}
