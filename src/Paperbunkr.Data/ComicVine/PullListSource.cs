namespace Paperbunkr.Data.ComicVine;

/// <summary>One release from a store-date query: what the weekly pull list is made of.</summary>
public sealed record PullListEntry(int IssueId, int SeriesId, string SeriesName, string IssueNumber, DateTime StoreDate, DateTime? CoverDate, string? ImageUrl);

/// <summary>A series' publisher and cross-reference ids, which a release list item doesn't carry.</summary>
public sealed record PullListSeriesInfo(int SeriesId, string Name, string? Publisher, int? YearBegan, int? ComicVineId);

/// <summary>A source that can list every release in a store-date window (today only Metron can; ComicVine's issue filter is too coarse to page a whole week reliably).</summary>
public interface IPullListSource
{
    /// <summary>Every issue with a store date from <paramref name="from"/> to <paramref name="to"/> inclusive, across all publishers.</summary>
    Task<IReadOnlyList<PullListEntry>> GetReleasesAsync(DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>The series behind a release, or <c>null</c> when the source has no such series.</summary>
    Task<PullListSeriesInfo?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken);
}
