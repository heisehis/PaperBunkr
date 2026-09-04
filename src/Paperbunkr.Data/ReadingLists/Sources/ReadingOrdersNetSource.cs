using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// readingorders.com - ported from the real, live-verified
/// <c>_reference/CBLManager/src/CBLManager/ReadingOrdersNetSource.cs</c>. A Next.js app whose
/// server-rendered HTML embeds React Server Component payloads containing real per-issue JSON.
/// Deliberately does a simple text-level regex extraction rather than reconstructing the full RSC
/// wire format (an internal Next.js serialization detail, not a stable target). No year extraction -
/// the payload's date field position relative to title varies between chunk formats observed live,
/// and <see cref="ReadingListMatcher"/> already treats year as an optional tie-breaker.
/// </summary>
public sealed class ReadingOrdersNetSource : IReadingListSource
{
    private const string BaseUrl = "https://www.readingorders.com";
    private const string ArcUrlPrefix = BaseUrl + "/reading-orders/";

    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _throttleLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    private List<ArcSearchResult>? _cachedIndex;

    public string SourceKey => "ReadingOrdersNet";
    public string DisplayName => "ReadingOrders.com";
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
        string html = await GetHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);
        return ParseIssuesFromHtml(html);
    }

    /// <summary>Pure parsing, split out from the network fetch so it's unit-testable against a literal HTML/RSC-payload fixture (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §6).</summary>
    public static List<ArcIssue> ParseIssuesFromHtml(string html)
    {
        var result = new List<ArcIssue>();

        // Matches the RSC-payload's escaped-quote JSON string form:
        // \"title\":\"Series #Number optional-subtitle\"
        foreach (Match m in Regex.Matches(html, @"\\""title\\"":\\""([^\\]*#[0-9]+[A-Za-z]?[^\\]*)\\"""))
        {
            string text = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            var issueMatch = Regex.Match(text, @"^(?<series>.+?)\s+#(?<number>\d+[A-Za-z]?)\b");
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
        string html = await GetHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);

        // The event object's own "description" field was confirmed present in the raw payload. No
        // cover-image field was spotted in the same sample, so CoverImageUrl stays null - not
        // confirmed to exist, unlike description.
        var descMatch = Regex.Match(html, @"\\""description\\"":\\""([^\\]*)\\""");
        string? description = descMatch.Success ? WebUtility.HtmlDecode(descMatch.Groups[1].Value).Trim() : null;

        return new ArcOverviewInfo(description, CoverImageUrl: null);
    }

    private async Task<List<ArcSearchResult>> LoadIndexAsync(CancellationToken cancellationToken)
    {
        string html = await GetHtmlAsync(BaseUrl + "/", cancellationToken).ConfigureAwait(false);
        var results = new List<ArcSearchResult>();

        foreach (Match m in Regex.Matches(html, @"\\""title\\"":\\""([^\\]*)\\"",\\""slug\\"":\\""([^\\]*)\\"""))
        {
            string name = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string slug = m.Groups[2].Value.Trim();
            string url = ArcUrlPrefix + slug;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(slug) || results.Any(e => e.Id == url))
            {
                continue;
            }

            results.Add(new ArcSearchResult(url, name, Deck: null, Publisher: null, IssueCount: 0));
        }

        return results;
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
            throw new ReadingListSourceException(DisplayName, $"ReadingOrders.com request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsing throws TaskCanceledException, not HttpRequestException.
            throw new ReadingListSourceException(DisplayName, "ReadingOrders.com did not respond within 20 seconds.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ReadingListSourceException(DisplayName, $"ReadingOrders.com returned {(int)response.StatusCode} {response.ReasonPhrase} for {url}");
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
