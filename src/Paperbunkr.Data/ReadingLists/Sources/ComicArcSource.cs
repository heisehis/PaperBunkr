using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// comicarc.com - ported from the real, live-verified
/// <c>_reference/CBLManager/src/CBLManager/ComicArcSource.cs</c>. The cleanest of the four Tier-2
/// sources: every reading-order page embeds a real schema.org JSON-LD <c>ItemList</c> (standard SEO
/// markup, one <c>ListItem</c> per issue in curated reading order) plus a sibling <c>Article</c>
/// block for description/cover - parsed with <see cref="JsonNode"/>, not per-field regex.
/// </summary>
public sealed class ComicArcSource : IReadingListSource
{
    private const string BaseUrl = "https://comicarc.com";

    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _throttleLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    private List<ArcSearchResult>? _cachedIndex;
    private readonly Dictionary<string, string> _htmlCache = new();

    public string SourceKey => "ComicArc";
    public string DisplayName => "ComicArc";
    public bool RequiresCredentials => false;
    public bool HasBrowsableCatalog => true;

    public async Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        _cachedIndex ??= await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        return _cachedIndex
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        var itemList = await GetJsonLdBlockAsync(arcId, "ItemList", cancellationToken).ConfigureAwait(false);
        return ParseIssuesFromItemList(itemList);
    }

    /// <summary>Pure parsing, split out from the network fetch so it's unit-testable against a literal JSON-LD fixture (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §6).</summary>
    public static List<ArcIssue> ParseIssuesFromItemList(JsonNode? itemList)
    {
        var result = new List<ArcIssue>();
        var items = itemList?["itemListElement"]?.AsArray();
        if (items is null)
        {
            return result;
        }

        foreach (var entry in items)
        {
            string? name = entry?["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Same series/number split used by every adapter. Confirmed against real entries
            // including "Absolute Batman 2025 Annual #1" and "Absolute Batman: Ark-M #1".
            var issueMatch = Regex.Match(name.Trim(), @"^(?<series>.+?)\s+#(?<number>[0-9]+[A-Za-z]?)$");
            if (!issueMatch.Success)
            {
                continue;
            }

            result.Add(new ArcIssue(issueMatch.Groups["series"].Value.Trim(), issueMatch.Groups["number"].Value, Year: 0, CoverImageUrl: null));
        }

        return result;
    }

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        var article = await GetJsonLdBlockAsync(arcId, "Article", cancellationToken).ConfigureAwait(false);
        if (article is null)
        {
            return null;
        }

        return new ArcOverviewInfo(article["description"]?.GetValue<string>(), article["image"]?.GetValue<string>());
    }

    private async Task<List<ArcSearchResult>> LoadIndexAsync(CancellationToken cancellationToken)
    {
        string sitemap = await GetHtmlAsync(BaseUrl + "/sitemap.xml", cancellationToken).ConfigureAwait(false);
        var urls = Regex.Matches(sitemap, @"<loc>(https://comicarc\.com/reading-orders/[^<]+)</loc>")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var results = new List<ArcSearchResult>();
        foreach (string url in urls)
        {
            var itemList = await GetJsonLdBlockAsync(url, "ItemList", cancellationToken).ConfigureAwait(false);
            var article = await GetJsonLdBlockAsync(url, "Article", cancellationToken).ConfigureAwait(false);
            string? name = itemList?["name"]?.GetValue<string>();
            if (itemList is null || string.IsNullOrEmpty(name))
            {
                continue;
            }

            int issueCount = itemList["numberOfItems"]?.GetValue<int>() ?? 0;

            // Publisher isn't part of either JSON-LD block - it's a separate prop embedded
            // elsewhere in the page's React payload (\"publisher\":\"DC Comics\"), best-effort only.
            string html = await GetCachedHtmlAsync(url, cancellationToken).ConfigureAwait(false);
            var publisherMatch = Regex.Match(html, @"\\""publisher\\"":\\""([^\\""]*)\\""");
            string? publisher = publisherMatch.Success ? publisherMatch.Groups[1].Value : null;

            string? deck = article?["description"]?.GetValue<string>();

            results.Add(new ArcSearchResult(url, name, deck, publisher, issueCount));
        }

        return results;
    }

    private async Task<JsonNode?> GetJsonLdBlockAsync(string url, string type, CancellationToken cancellationToken)
    {
        string html = await GetCachedHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        foreach (Match m in Regex.Matches(html, @"<script type=""application/ld\+json"">(.*?)</script>", RegexOptions.Singleline))
        {
            JsonNode? block;
            try
            {
                block = JsonNode.Parse(m.Groups[1].Value);
            }
            catch (JsonException)
            {
                continue; // not every ld+json block on the page is the type we want (BreadcrumbList/FAQPage etc.) - a malformed block shouldn't abort the whole page
            }

            if (string.Equals(block?["@type"]?.GetValue<string>(), type, StringComparison.Ordinal))
            {
                return block;
            }
        }

        return null;
    }

    private async Task<string> GetCachedHtmlAsync(string url, CancellationToken cancellationToken)
    {
        if (_htmlCache.TryGetValue(url, out string? html))
        {
            return html;
        }

        html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        _htmlCache[url] = html;
        return html;
    }

    private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReadingListSourceException(DisplayName, $"ComicArc request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsing throws TaskCanceledException, not HttpRequestException.
            throw new ReadingListSourceException(DisplayName, "ComicArc did not respond within 20 seconds.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ReadingListSourceException(DisplayName, $"ComicArc returned {(int)response.StatusCode} {response.ReasonPhrase} for {url}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
