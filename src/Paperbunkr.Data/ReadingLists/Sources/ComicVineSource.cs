using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// Thin wrapper over the ComicVine REST API (docs/superpowers/specs/2026-08-22-cbl-manager-arc-
/// lookup-design.md §3) - ported from the real, live-verified
/// <c>_reference/CBLManager/src/CBLManager/ComicVineClient.cs</c>, translated from its synchronous
/// .NET Framework <c>HttpClient</c> calls to async. Endpoint quirks (the generic <c>/search/</c>
/// endpoint silently returning empty, the batch-issues endpoint not preserving requested order) are
/// carried over as-is from that already-verified source, not re-derived.
/// </summary>
public sealed class ComicVineSource : IReadingListSource
{
    private const string BaseUrl = "https://comicvine.gamespot.com/api";
    private const int BatchChunkSize = 40;

    private static readonly HttpClient Http = CreateClient();

    // ComicVine documents a ~200 req/hour limit; a simple minimum spacing between requests is a
    // cheap, good-enough courtesy throttle for the handful of calls one user action makes.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1);

    private readonly string _apiKey;
    private readonly object _throttleLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public ComicVineSource(string apiKey)
    {
        _apiKey = apiKey;
    }

    public string SourceKey => "ComicVine";
    public string DisplayName => "ComicVine";
    public bool RequiresCredentials => true;
    public bool HasBrowsableCatalog => false;

    public async Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // The generic /search/?resources=story_arc endpoint silently returns an empty results
        // array (while still reporting a nonzero number_of_total_results) - confirmed dead end in
        // the reference source; the dedicated /story_arcs/ filter endpoint is the real path.
        string url = $"{BaseUrl}/story_arcs/?api_key={Uri.EscapeDataString(_apiKey)}&format=json" +
                      $"&filter=name:{Uri.EscapeDataString(query)}" +
                      "&field_list=id,name,deck,publisher,count_of_issue_appearances&limit=20";

        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var results = root?["results"]?.AsArray() ?? new JsonArray();

        var list = new List<ArcSearchResult>();
        foreach (var node in results)
        {
            if (node is null)
            {
                continue;
            }

            list.Add(new ArcSearchResult(
                Id: node["id"]!.ToString(),
                Name: node["name"]?.GetValue<string>() ?? string.Empty,
                Deck: node["deck"]?.GetValue<string>(),
                Publisher: node["publisher"]?["name"]?.GetValue<string>(),
                IssueCount: node["count_of_issue_appearances"]?.GetValue<int>() ?? 0));
        }

        return list;
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        // Step 1: the story arc's own "issues" field gives id+name stubs already in the site's
        // curated reading order - this order is what we preserve.
        string detailUrl = $"{BaseUrl}/story_arc/4045-{Uri.EscapeDataString(arcId)}/?api_key={Uri.EscapeDataString(_apiKey)}" +
                            "&format=json&field_list=id,name,issues";
        var detailRoot = await GetJsonAsync(detailUrl, cancellationToken).ConfigureAwait(false);
        var issueStubs = detailRoot?["results"]?["issues"]?.AsArray() ?? new JsonArray();

        var orderedIds = issueStubs
            .Where(s => s is not null)
            .Select(s => s!["id"]!.GetValue<int>())
            .ToList();

        if (orderedIds.Count == 0)
        {
            return Array.Empty<ArcIssue>();
        }

        // Step 2: batch-fetch the real issue_number/volume/date fields (chunked - very large arcs
        // could otherwise build an unreasonably long URL).
        var detailsById = new Dictionary<int, ArcIssue>();
        foreach (var chunk in Chunk(orderedIds, BatchChunkSize))
        {
            string idsParam = string.Join("|", chunk);
            string issuesUrl = $"{BaseUrl}/issues/?api_key={Uri.EscapeDataString(_apiKey)}&format=json" +
                                $"&filter=id:{idsParam}" +
                                "&field_list=id,issue_number,volume,cover_date,store_date,image" +
                                $"&limit={BatchChunkSize}";

            var issuesRoot = await GetJsonAsync(issuesUrl, cancellationToken).ConfigureAwait(false);
            foreach (var node in issuesRoot?["results"]?.AsArray() ?? new JsonArray())
            {
                if (node is null)
                {
                    continue;
                }

                int id = node["id"]!.GetValue<int>();
                string? coverDate = node["cover_date"]?.GetValue<string>();
                string? storeDate = node["store_date"]?.GetValue<string>();
                int year = ParseYearFromDate(coverDate) ?? ParseYearFromDate(storeDate) ?? 0;

                detailsById[id] = new ArcIssue(
                    Series: node["volume"]?["name"]?.GetValue<string>() ?? string.Empty,
                    Number: node["issue_number"]?.GetValue<string>() ?? string.Empty,
                    Year: year,
                    CoverImageUrl: node["image"]?["small_url"]?.GetValue<string>());
            }
        }

        // Step 3: reassemble in the arc's original curated order - the batch endpoint does not
        // preserve the requested id order.
        return orderedIds
            .Where(detailsById.ContainsKey)
            .Select(id => detailsById[id])
            .ToList();
    }

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        string url = $"{BaseUrl}/story_arc/4045-{Uri.EscapeDataString(arcId)}/?api_key={Uri.EscapeDataString(_apiKey)}" +
                      "&format=json&field_list=deck,description,image";
        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var result = root?["results"];
        if (result is null)
        {
            return null;
        }

        // "description" is real HTML (confirmed live against ComicVine's actual response shape) -
        // stripped once here, at the source, rather than at each display site.
        string? description = result["description"]?.GetValue<string>();
        description = string.IsNullOrEmpty(description) ? result["deck"]?.GetValue<string>() : StripHtml(description);

        return new ArcOverviewInfo(description, result["image"]?["medium_url"]?.GetValue<string>());
    }

    private static string StripHtml(string html)
    {
        string text = Regex.Replace(html, @"<br\s*/?>|</p>|</h[1-6]>|</li>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\n{3,}", "\n\n").Trim();
    }

    private static int? ParseYearFromDate(string? date)
    {
        if (!string.IsNullOrEmpty(date) && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out int year))
        {
            return year;
        }
        return null;
    }

    private static IEnumerable<List<int>> Chunk(List<int> source, int chunkSize)
    {
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        string body;
        try
        {
            body = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReadingListSourceException(DisplayName, $"ComicVine request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsing throws TaskCanceledException, not HttpRequestException.
            throw new ReadingListSourceException(DisplayName, "ComicVine did not respond within 20 seconds.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ReadingListSourceException(DisplayName, $"ComicVine returned an unexpected response: {ex.Message}");
        }

        int statusCode = root?["status_code"]?.GetValue<int>() ?? 0;
        if (statusCode != 1)
        {
            string? error = root?["error"]?.GetValue<string>();
            throw new ReadingListSourceException(DisplayName, $"ComicVine API error {statusCode}: {error}");
        }

        return root;
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (_throttleLock)
        {
            delay = MinRequestInterval - (DateTime.UtcNow - _lastRequestUtc);
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        lock (_throttleLock)
        {
            _lastRequestUtc = DateTime.UtcNow;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // ComicVine rejects requests without a real User-Agent header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/0.1 (comic library manager)");
        return client;
    }
}
