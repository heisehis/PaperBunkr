using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// What the series connector asks Metron (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.1), so tests can fake
/// it - same pattern as <see cref="IMetronEventLists"/>. <see cref="MetronClient"/> is the real one. Metron's series list filters on both
/// <c>cv_id</c> and <c>gcd_id</c> (checked against Metron's <c>comicsdb/filters/series.py</c>, 2026-10-29).
/// </summary>
public interface IMetronSeriesLinks
{
    /// <summary>A Metron series' <c>associated</c> list (spin-offs, companion series).</summary>
    Task<IReadOnlyList<ComicVineIdName>> GetAssociatedSeriesAsync(int metronSeriesId, CancellationToken cancellationToken);

    /// <summary>Metron series whose ComicVine id is <paramref name="comicVineSeriesId"/>.</summary>
    Task<IReadOnlyList<ComicSeriesHit>> FindSeriesByComicVineIdAsync(int comicVineSeriesId, CancellationToken cancellationToken);

    /// <summary>Metron series whose Grand Comics Database id is <paramref name="gcdSeriesId"/>.</summary>
    Task<IReadOnlyList<ComicSeriesHit>> FindSeriesByGcdIdAsync(int gcdSeriesId, CancellationToken cancellationToken);

    /// <summary>One Metron series' record, including its <c>cv_id</c> and <c>gcd_id</c>; null when it doesn't exist.</summary>
    Task<PullListSeriesInfo?> GetSeriesInfoAsync(int metronSeriesId, CancellationToken cancellationToken);
}
