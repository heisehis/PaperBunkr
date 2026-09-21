using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.Data.ComicVine;

/// <summary>What a local series tells us about which ComicVine volume it is: every field is optional, and the more there is the better the ranking.</summary>
/// <param name="Name">The name to match (what the user is searching for).</param>
/// <param name="Year">The earliest year among the series' own issues; a volume that started after it can't be the right one.</param>
/// <param name="HighestIssueNumber">The highest whole issue number owned; a volume with fewer issues than that is unlikely.</param>
/// <param name="Publisher">The series' publisher, when known.</param>
public sealed record LocalSeriesHints(string Name, int? Year = null, int? HighestIssueNumber = null, string? Publisher = null);

public sealed record RankedVolume(ComicVineVolume Volume, double Score);

/// <summary>
/// Orders ComicVine volumes by how likely each is to be a given local series. ComicVine's own order (here: most issues first) is a poor guess on its own: it surfaces every long-running
/// old run that shares a word with the name and buries a short recent series. This reuses the scraper's match scoring (CE's ComicVineScraper formula: name overlap, year plausibility,
/// issue-count plausibility, mirror-publisher penalty, recency), plus a bonus when the publisher agrees, so the two features rank the same way.
/// </summary>
public static class VolumeMatchRanker
{
    /// <summary>Added when the volume's publisher matches the local one (the strongest cheap signal the scoring formula doesn't use).</summary>
    public const double PublisherAgreementBonus = 8;

    public static IReadOnlyList<RankedVolume> Rank(IEnumerable<ComicVineVolume> volumes, LocalSeriesHints hints, int currentYear)
    {
        return volumes
            .Select(v => new RankedVolume(v, Score(v, hints, currentYear)))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Volume.CountOfIssues)     // among equals, the fuller run
            .ThenBy(r => r.Volume.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static double Score(ComicVineVolume volume, LocalSeriesHints hints, int currentYear)
    {
        var candidate = new ComicVineVolumeSearchResult(
            volume.Id, volume.Name, volume.StartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture), volume.Publisher, volume.CountOfIssues, volume.ImageUrl);

        double score = MatchScoreCalculator.Compute(hints.Name, bookFormat: null, hints.HighestIssueNumber, hints.Year, candidate, wasPreviouslyChosenForSimilarBook: false, currentYear);

        if (!string.IsNullOrWhiteSpace(hints.Publisher) && !string.IsNullOrWhiteSpace(volume.Publisher)
            && string.Equals(hints.Publisher.Trim(), volume.Publisher.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += PublisherAgreementBonus;
        }

        return score;
    }
}

/// <summary>Volume search with more than one page of results (kept apart from <see cref="IComicVineClient"/> so its fakes needn't implement it).</summary>
public interface IComicVineVolumeSearch
{
    /// <summary>Volumes whose name contains <paramref name="query"/>, up to <paramref name="maxResults"/>, most issues first. Costs one request per 100 results.</summary>
    Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, int maxResults, CancellationToken cancellationToken);
}

/// <summary>The one entry point both series pickers use: search ComicVine (as many pages as the client supports), then rank against what is known locally.</summary>
public static class VolumeSearchService
{
    /// <summary>How many volumes are pulled and ranked (3 requests at ComicVine's page size).</summary>
    public const int MaxVolumesConsidered = 300;

    public static async Task<IReadOnlyList<RankedVolume>> SearchAndRankAsync(
        IComicVineClient client, string query, LocalSeriesHints hints, CancellationToken cancellationToken, int? currentYear = null)
    {
        var volumes = client is IComicVineVolumeSearch paged
            ? await paged.SearchVolumesAsync(query, MaxVolumesConsidered, cancellationToken).ConfigureAwait(false)
            : await client.SearchVolumesAsync(query, cancellationToken).ConfigureAwait(false);

        return VolumeMatchRanker.Rank(volumes, hints, currentYear ?? DateTime.UtcNow.Year);
    }
}
