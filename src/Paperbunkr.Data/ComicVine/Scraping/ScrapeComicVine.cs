namespace Paperbunkr.Data.ComicVine.Scraping;

// Search-side types the scraper's ranking and review flow use (ported from the plugin's ComicVineService DTOs). The per-issue detail types
// (ComicVineIssueDetails, ComicVineCredit, ComicVineDatePart) are core's own, in ComicVineIssueDetails.cs.

public sealed record ComicVineVolumeSearchResult(int Id, string Name, string? StartYear, string? Publisher, int? CountOfIssues, string? ImageUrl, string? Status = null, string? Description = null);

public sealed record ComicVineVolumeDetails(int Id, string Name, string? StartYear, string? Publisher, int? CountOfIssues, string? ImageUrl, string? Status = null, string? Description = null);

public sealed record ComicVineIssueSummary(int Id, string? IssueNumber, string? Name, string? ImageUrl, string? CoverHash = null)
{
    public IReadOnlyList<string> AlternateCoverUrls { get; init; } = Array.Empty<string>();
}

/// <summary>How the issue-match dialog resolved: a plain "chosen issue or null" can't tell Skip (apply volume-level fields only) from Go Back (re-show the series dialog).</summary>
public enum ComicVineIssueReviewOutcome
{
    Confirmed,
    Skipped,
    WentBack,
}

public sealed record ComicVineIssueReviewResult(ComicVineIssueReviewOutcome Outcome, ComicVineIssueSummary? Issue)
{
    public static readonly ComicVineIssueReviewResult Skipped = new(ComicVineIssueReviewOutcome.Skipped, null);
    public static readonly ComicVineIssueReviewResult WentBack = new(ComicVineIssueReviewOutcome.WentBack, null);

    public static ComicVineIssueReviewResult Confirmed(ComicVineIssueSummary issue) => new(ComicVineIssueReviewOutcome.Confirmed, issue);
}

/// <summary>Series found through one of their issues (<see cref="IIssueFirstSeriesSearch"/>), for the scraper's candidate list.</summary>
public interface IScrapeIssueFirstSearch
{
    Task<IReadOnlyList<ComicVineVolumeSearchResult>> FindSeriesByIssueAsync(string seriesName, string issueNumber, int? year, CancellationToken cancellationToken = default);
}

/// <summary>What the scraper needs from ComicVine. One implementation over the shared, rate-limited client (<see cref="ScrapeComicVineAdapter"/>); tests use fakes.</summary>
public interface IScrapeComicVine
{
    Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default);

    Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default);

    Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default);

    /// <summary>Every issue of a story arc with its dates, for reading-order positions. Empty for a source with no such data (Metron), so those fields simply stay unwritten.</summary>
    Task<IReadOnlyList<StoryArcIssue>> GetStoryArcIssuesAsync(int storyArcId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoryArcIssue>>(Array.Empty<StoryArcIssue>());
}

/// <summary>A source that can list a story arc's issues with their dates (ComicVine).</summary>
public interface IStoryArcSource
{
    Task<IReadOnlyList<StoryArcIssue>> GetStoryArcIssuesAsync(int storyArcId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IScrapeComicVine"/> over core's <see cref="IComicVineClient"/> (so every scrape shares the one rate-limit budget and priority scheme; the plugin
/// had its own HTTP client and throttle). ComicVine returns a volume's issues in one paginated sweep, so <see cref="SearchIssuesAsync"/> returns them all for page 1
/// and nothing after, which is what the orchestrator's paging loop expects.
/// </summary>
/// <remarks>
/// With a <see cref="Caching.ProviderCache"/>, series searches, volume details, issue lists and issue details are read through it
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md section 3): the next book of the same series, a retry or a
/// re-scrape asks the provider nothing. When the provider is rate-limited (code 107), an expired entry is used rather than failing.
/// </remarks>
/// <param name="searchVolumesFirst">
/// The app's fuller searches (2026-10-22): ComicVine searches /volumes by name (both ends of the list) before /search, Metron takes 100 results
/// instead of 25, and series are also found through an issue number (<see cref="FindSeriesByIssueAsync"/>). Off for tests that script requests.
/// </param>
public sealed class ScrapeComicVineAdapter(IComicVineClient client, IComicVineIssueDetailsSource details, Caching.ProviderCache? cache = null, bool searchVolumesFirst = false)
    : IScrapeComicVine, IScrapeIssueFirstSearch, IComicIssueLookup, Metadata.IMetronUniverses
{
    /// <summary>
    /// Series found through an issue: off unless <c>searchVolumesFirst</c> (the app's searches) is on. Cached like any search; ComicVine's results
    /// get their publisher, issue count and cover from one volume lookup each for the first five.
    /// </summary>
    public async Task<IReadOnlyList<ComicVineVolumeSearchResult>> FindSeriesByIssueAsync(string seriesName, string issueNumber, int? year, CancellationToken cancellationToken = default)
    {
        if (!searchVolumesFirst || client is not IIssueFirstSeriesSearch byIssue || string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(issueNumber))
        {
            return Array.Empty<ComicVineVolumeSearchResult>();
        }

        string key = $"issue|{seriesName.Trim().ToLowerInvariant()}|{issueNumber.Trim()}|{year}";
        return await CachedAsync<List<ComicVineVolumeSearchResult>>(Caching.ProviderCacheKind.SeriesSearch, key, async () =>
        {
            IReadOnlyList<ComicVineVolume> found = await byIssue.FindSeriesByIssueAsync(seriesName, issueNumber.Trim(), year, cancellationToken).ConfigureAwait(false);
            if (_cacheProvider == nameof(Entities.ComicProvider.ComicVine))
            {
                found = await FillDegradedAsync(found, cancellationToken).ConfigureAwait(false);
            }

            // An issue count of 0 here means "not known", not "empty": scored as unknown so a match isn't ruled out for it.
            return found.Select(v => new ComicVineVolumeSearchResult(v.Id, v.Name, v.StartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture), v.Publisher,
                v.CountOfIssues > 0 ? v.CountOfIssues : null, v.ImageUrl, v.Status, v.Description)).ToList();
        }).ConfigureAwait(false);
    }

    private readonly string _cacheProvider = (client as IComicProvider)?.Kind.ToString() ?? client.GetType().Name;

    /// <summary>The shapes of a series name tried against ComicVine's /volumes, most specific first, without repeats.</summary>
    internal static IReadOnlyList<string> NameVariants(string query)
    {
        var variants = new List<string>();
        void Add(string candidate)
        {
            string trimmed = candidate.Trim(' ', ':', '-', '–');
            if (trimmed.Length >= 2 && !variants.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                variants.Add(trimmed);
            }
        }

        string withoutThe = System.Text.RegularExpressions.Regex.Replace(query.Trim(), @"^the\s+", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Add(query);
        Add(withoutThe);
        int colon = withoutThe.IndexOf(':');
        if (colon > 0)
        {
            Add(withoutThe[..colon]);
            Add(withoutThe[(colon + 1)..]);
        }

        return variants;
    }

    /// <summary>
    /// ComicVine's /search answering without publisher, issue count or cover (seen 2026-10-22): the first few results are looked up one by one
    /// (one request each) so the ones a person is likely to pick show what they are; the rest stay as they came.
    /// </summary>
    private async Task<IReadOnlyList<ComicVineVolume>> FillDegradedAsync(IReadOnlyList<ComicVineVolume> volumes, CancellationToken cancellationToken)
    {
        const int Fill = 5;
        if (volumes.Count == 0 || volumes.Any(v => v.Publisher is not null || v.CountOfIssues > 0))
        {
            return volumes;
        }

        var filled = volumes.ToList();
        for (int i = 0; i < Math.Min(Fill, filled.Count); i++)
        {
            try
            {
                if (await client.GetVolumeAsync(filled[i].Id, cancellationToken).ConfigureAwait(false) is { } full)
                {
                    filled[i] = full with { Description = full.Description ?? filled[i].Description };
                }
            }
            catch (ComicVineException)
            {
                break; // ComicVine struggling: keep what came back
            }
        }

        return filled;
    }

    /// <summary>A search answer where no volume has a publisher or any issues: what ComicVine's failing /search returned, not worth keeping.</summary>
    private static bool LooksDegraded(List<ComicVineVolumeSearchResult> results) =>
        results.Count > 0 && results.All(r => r.Publisher is null && (r.CountOfIssues ?? 0) == 0);

    private async Task<T> CachedAsync<T>(Caching.ProviderCacheKind kind, string key, Func<Task<T>> fetch, Func<T, bool>? keep = null)
        where T : class
    {
        if (cache is null)
        {
            return await fetch().ConfigureAwait(false);
        }

        if (cache.TryGet<T>(_cacheProvider, kind, key, out var hit) && hit is not null)
        {
            return hit;
        }

        try
        {
            var fresh = await fetch().ConfigureAwait(false);
            if (keep is null || keep(fresh))
            {
                cache.Set(_cacheProvider, kind, key, fresh);
            }

            return fresh;
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 107 && cache.TryGetStale<T>(_cacheProvider, kind, key, out var stale) && stale is not null)
        {
            return stale;
        }
    }

    public Task<IReadOnlyList<Metadata.MetronUniverseInfo>> SearchUniversesAsync(string query, CancellationToken cancellationToken) =>
        client is Metadata.IMetronUniverses source
            ? source.SearchUniversesAsync(query, cancellationToken)
            : Task.FromResult<IReadOnlyList<Metadata.MetronUniverseInfo>>(Array.Empty<Metadata.MetronUniverseInfo>());

    public Task<Metadata.MetronUniverseInfo?> GetUniverseAsync(int universeId, CancellationToken cancellationToken) =>
        client is Metadata.IMetronUniverses source
            ? source.GetUniverseAsync(universeId, cancellationToken)
            : Task.FromResult<Metadata.MetronUniverseInfo?>(null);

    // Exact lookups exist on Metron only; any other source answers "nothing", which sends the scraper on to its name search.
    public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByComicVineIdAsync(int comicVineIssueId, CancellationToken cancellationToken) =>
        client is IComicIssueLookup lookup
            ? lookup.FindIssuesByComicVineIdAsync(comicVineIssueId, cancellationToken)
            : Task.FromResult<IReadOnlyList<ComicIssueHit>>(Array.Empty<ComicIssueHit>());

    public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByUpcAsync(string upc, CancellationToken cancellationToken) =>
        client is IComicIssueLookup lookup
            ? lookup.FindIssuesByUpcAsync(upc, cancellationToken)
            : Task.FromResult<IReadOnlyList<ComicIssueHit>>(Array.Empty<ComicIssueHit>());

    public async Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
    {
        if (page > 1)
        {
            return Array.Empty<ComicVineVolumeSearchResult>();
        }

        // "v2|": entries saved before 2026-10-22 may hold ComicVine's degraded search answers (below) and are not reused.
        return await CachedAsync<List<ComicVineVolumeSearchResult>>(Caching.ProviderCacheKind.SeriesSearch, "v2|" + query.Trim().ToLowerInvariant(), async () =>
        {
            // ComicVine's /search endpoint degraded on 2026-10-22: it timed out or answered with no publisher, 0 issues and the "blank" cover for
            // every volume (seen live; its /volumes endpoint answered the same query fully). So ComicVine searches /volumes by name first - two
            // pages, most issues first, enough for a short new series under a busy name - and falls back to /search only when that finds nothing
            // (a name with words in another order). Metron keeps its own search.
            IReadOnlyList<ComicVineVolume> volumes = Array.Empty<ComicVineVolume>();
            bool byNameFirst = searchVolumesFirst && _cacheProvider == nameof(Entities.ComicProvider.ComicVine) && client is IComicVineVolumeSearch;
            if (byNameFirst)
            {
                // /volumes matches only names that contain the whole query, so a few shapes of the name are tried before giving up on it:
                // "The Punisher Showdowns: Black Widow" -> without "The" -> "Punisher Showdowns" (before the colon) -> "Black Widow" (after it).
                foreach (string variant in NameVariants(query))
                {
                    // Both ends of the list: ComicVine returns matches oldest first whatever the sort asks, so the newest series of a busy name sit at the end.
                    volumes = client is ComicVineClient comicVine
                        ? await comicVine.SearchVolumesBothEndsAsync(variant, 200, cancellationToken).ConfigureAwait(false)
                        : await ((IComicVineVolumeSearch)client).SearchVolumesAsync(variant, 200, cancellationToken).ConfigureAwait(false);
                    if (volumes.Count > 0)
                    {
                        break;
                    }
                }
            }

            if (volumes.Count == 0 && searchVolumesFirst && _cacheProvider == nameof(Entities.ComicProvider.Metron) && client is IComicVineVolumeSearch metronByName)
            {
                // Metron's plain search stops at 25, alphabetically: "X-Men" was crowded out by "All-New X-Men", "Astonishing X-Men"... One page of 100.
                volumes = await metronByName.SearchVolumesAsync(query, 100, cancellationToken).ConfigureAwait(false);
            }

            if (volumes.Count == 0)
            {
                volumes = await client.SearchVolumesFuzzyAsync(query, cancellationToken).ConfigureAwait(false);
                if (byNameFirst)
                {
                    volumes = await FillDegradedAsync(volumes, cancellationToken).ConfigureAwait(false);
                }
            }

            return volumes.Select(v => new ComicVineVolumeSearchResult(v.Id, v.Name, v.StartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture), v.Publisher, v.CountOfIssues, v.ImageUrl, v.Status, v.Description)).ToList();
        }, keep: results => !LooksDegraded(results)).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<StoryArcIssue>> GetStoryArcIssuesAsync(int storyArcId, CancellationToken cancellationToken = default) =>
        client is IStoryArcSource source
            ? source.GetStoryArcIssuesAsync(storyArcId, cancellationToken)
            : Task.FromResult<IReadOnlyList<StoryArcIssue>>(Array.Empty<StoryArcIssue>());

    public async Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default)
    {
        var found = await CachedAsync<ComicVineVolumeDetails[]>(Caching.ProviderCacheKind.VolumeDetails, volumeId.ToString(System.Globalization.CultureInfo.InvariantCulture), async () =>
        {
            var v = await client.GetVolumeAsync(volumeId, cancellationToken).ConfigureAwait(false);
            return v is null
                ? Array.Empty<ComicVineVolumeDetails>()
                : new[] { new ComicVineVolumeDetails(v.Id, v.Name, v.StartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture), v.Publisher, v.CountOfIssues, v.ImageUrl, v.Status, v.Description) };
        }).ConfigureAwait(false);
        return found.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (page > 1)
        {
            return Array.Empty<ComicVineIssueSummary>();
        }

        // The key version invalidates pre-alternate-cover cache rows, which only stored ImageUrl.
        return await CachedAsync<List<ComicVineIssueSummary>>(Caching.ProviderCacheKind.VolumeIssueList, $"covers-v1|{volumeId}", async () =>
        {
            var issues = await client.GetVolumeIssuesAsync(volumeId, cancellationToken).ConfigureAwait(false);
            return issues.Select(i => new ComicVineIssueSummary(i.Id, i.IssueNumber, i.Name, i.ImageUrl, i.CoverHash)
            {
                AlternateCoverUrls = i.AlternateCoverUrls,
            }).ToList();
        }).ConfigureAwait(false);
    }

    public async Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default)
    {
        var found = await CachedAsync<ComicVineIssueDetails[]>(Caching.ProviderCacheKind.IssueDetails, $"covers-v1|{issueId}", async () =>
        {
            var d = await details.GetIssueDetailsAsync(issueId, cancellationToken).ConfigureAwait(false);
            return d is null ? Array.Empty<ComicVineIssueDetails>() : new[] { d };
        }).ConfigureAwait(false);
        return found.FirstOrDefault();
    }
}
