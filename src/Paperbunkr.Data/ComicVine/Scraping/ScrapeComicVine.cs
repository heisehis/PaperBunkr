namespace Paperbunkr.Data.ComicVine.Scraping;

// Search-side types the scraper's ranking and review flow use (ported from the plugin's ComicVineService DTOs). The per-issue detail types
// (ComicVineIssueDetails, ComicVineCredit, ComicVineDatePart) are core's own, in ComicVineIssueDetails.cs.

public sealed record ComicVineVolumeSearchResult(int Id, string Name, string? StartYear, string? Publisher, int? CountOfIssues, string? ImageUrl);

public sealed record ComicVineVolumeDetails(int Id, string Name, string? StartYear, string? Publisher, int? CountOfIssues, string? ImageUrl);

public sealed record ComicVineIssueSummary(int Id, string? IssueNumber, string? Name, string? ImageUrl, string? CoverHash = null);

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
public sealed class ScrapeComicVineAdapter(IComicVineClient client, IComicVineIssueDetailsSource details) : IScrapeComicVine, IComicIssueLookup
{
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

        var volumes = await client.SearchVolumesFuzzyAsync(query, cancellationToken).ConfigureAwait(false);
        return volumes.Select(v => new ComicVineVolumeSearchResult(v.Id, v.Name, v.StartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture), v.Publisher, v.CountOfIssues, v.ImageUrl)).ToList();
    }

    public Task<IReadOnlyList<StoryArcIssue>> GetStoryArcIssuesAsync(int storyArcId, CancellationToken cancellationToken = default) =>
        client is IStoryArcSource source
            ? source.GetStoryArcIssuesAsync(storyArcId, cancellationToken)
            : Task.FromResult<IReadOnlyList<StoryArcIssue>>(Array.Empty<StoryArcIssue>());

    public async Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default)
    {
        var v = await client.GetVolumeAsync(volumeId, cancellationToken).ConfigureAwait(false);
        return v is null ? null : new ComicVineVolumeDetails(v.Id, v.Name, v.StartYear?.ToString(System.Globalization.CultureInfo.InvariantCulture), v.Publisher, v.CountOfIssues, v.ImageUrl);
    }

    public async Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (page > 1)
        {
            return Array.Empty<ComicVineIssueSummary>();
        }

        var issues = await client.GetVolumeIssuesAsync(volumeId, cancellationToken).ConfigureAwait(false);
        return issues.Select(i => new ComicVineIssueSummary(i.Id, i.IssueNumber, i.Name, i.ImageUrl, i.CoverHash)).ToList();
    }

    public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) =>
        details.GetIssueDetailsAsync(issueId, cancellationToken);
}
