using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// comicbookreadingorders.com - ported from the real, live-verified
/// <c>_reference/CBLManager/src/CBLManager/CBReadingOrdersSource.cs</c>. HTML-scraping, not a
/// structured API - the site has no CSV/JSON export of its own (the "download as Excel/CSV" tools
/// people reference are third-party scrapers of the same HTML). Scoped to cross-title "Events"
/// only (e.g. "Absolute Power", "Sinestro Corps War") - single-character arcs like "Hush" live
/// inside giant, unstructured "Master Reading Order" pages this adapter doesn't parse.
/// </summary>
public sealed class ComicBookReadingOrdersSource : IReadingListSource
{
    private const string BaseUrl = "https://comicbookreadingorders.com";

    private static readonly string[] EventTimelineUrls =
    {
        BaseUrl + "/dc/event-timeline/",
        BaseUrl + "/marvel/event-timeline/",
    };

    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _throttleLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    // Event index pages only change when the site adds a new event - cached per adapter instance
    // so repeated searches within one arc-search session don't re-fetch both index pages each time.
    private List<ArcSearchResult>? _cachedEvents;

    public string SourceKey => "ComicBookReadingOrders";
    public string DisplayName => "Comic Book Reading Orders";
    public bool RequiresCredentials => false;
    public bool HasBrowsableCatalog => true;

    public async Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        _cachedEvents ??= await LoadEventIndexAsync(cancellationToken).ConfigureAwait(false);
        return _cachedEvents
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        // arcId is the arc's own page URL - simplest stable identifier this site offers, since it
        // has no numeric resource ids anywhere.
        string html = await GetHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);
        return ParseIssuesFromHtml(html);
    }

    /// <summary>Pure parsing, split out from the network fetch so it's unit-testable against a literal HTML fixture (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §6). Handles both real markup styles this site uses (per-issue &lt;p&gt; wrapping, and bare &lt;span&gt; runs with no wrapper).</summary>
    public static List<ArcIssue> ParseIssuesFromHtml(string html)
    {
        // The site's markup for the issue list is NOT consistent across arcs - some pages wrap
        // every issue in its own <p>issue</p>, others just run <span style="color:...">issue</span>
        // (year)<br/> sequences with no per-issue <p> wrapper at all. A tag-agnostic, line-based
        // approach handles both: normalize every plausible boundary into a newline, strip
        // remaining tags, treat each resulting line as one candidate issue entry.
        html = Regex.Replace(html, @"<style[^>]*>.*?</style>", string.Empty, RegexOptions.Singleline);
        html = Regex.Replace(html, @"<script[^>]*>.*?</script>", string.Empty, RegexOptions.Singleline);

        // A blue span is a comment/annotation ("Takes place during X"), never an issue - drop it
        // entirely before line-splitting.
        html = Regex.Replace(html, @"<span style=""color:\s*#0000ff;?"">.*?</span>", string.Empty, RegexOptions.Singleline);

        html = Regex.Replace(html,
            @"</p>|<br\s*/?>|</h[1-6]>|</strong>|</b>|<span style=""color:",
            "\n<span style=\"color:",
            RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", string.Empty);

        var result = new List<ArcIssue>();
        foreach (string line in html.Split('\n'))
        {
            string text = WebUtility.HtmlDecode(line).Trim();
            var issueMatch = Regex.Match(text, @"^(?<series>.+?)\s+#(?<number>\d+[A-Za-z]?)\b(?<rest>.*)$");
            if (!issueMatch.Success)
            {
                continue;
            }

            int year = 0;
            var yearMatch = Regex.Match(issueMatch.Groups["rest"].Value, @"\((\d{4})\)");
            if (yearMatch.Success)
            {
                int.TryParse(yearMatch.Groups[1].Value, out year);
            }

            result.Add(new ArcIssue(issueMatch.Groups["series"].Value.Trim(), issueMatch.Groups["number"].Value, year, CoverImageUrl: null));
        }

        return result;
    }

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        string html = await GetHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);

        // The intro synopsis is the page's one <p style="text-align: justify;">...</p> paragraph,
        // confirmed present on every arc page checked. The site's own og:image meta tag gives a
        // real cover/banner image without needing to guess a CSS selector.
        var descMatch = Regex.Match(html, @"<p style=""text-align:\s*justify;?"">(.*?)</p>", RegexOptions.Singleline);
        string? description = descMatch.Success
            ? WebUtility.HtmlDecode(Regex.Replace(descMatch.Groups[1].Value, "<[^>]+>", string.Empty)).Trim()
            : null;

        var imageMatch = Regex.Match(html, @"<meta property=""og:image"" content=""([^""]+)""");
        string? imageUrl = imageMatch.Success ? WebUtility.HtmlDecode(imageMatch.Groups[1].Value) : null;

        return new ArcOverviewInfo(description, imageUrl);
    }

    private async Task<List<ArcSearchResult>> LoadEventIndexAsync(CancellationToken cancellationToken)
    {
        var events = new List<ArcSearchResult>();
        foreach (string indexUrl in EventTimelineUrls)
        {
            string publisher = indexUrl.Contains("/dc/") ? "DC" : "Marvel";
            string html = await GetHtmlAsync(indexUrl, cancellationToken).ConfigureAwait(false);

            // <p><a ... href="https://comicbookreadingorders.com/{pub}/events/{slug}-reading-order/">
            // Name</a> (Year)</p> - match on the href path shape rather than any class name, since
            // the <a>'s class attribute is inconsistent (seen before/after href, and sometimes
            // absent for a handful of unlinked plain-text entries with no dedicated page).
            foreach (Match m in Regex.Matches(html,
                @"<a[^>]+href=""(https://comicbookreadingorders\.com/(?:marvel|dc)/events/[^""]+)""[^>]*>([^<]+)</a>\s*(?:&#8211;)?\s*(?:\((\d{4})\))?"))
            {
                string url = m.Groups[1].Value;
                string name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                if (string.IsNullOrEmpty(name) || events.Any(e => e.Id == url))
                {
                    continue;
                }

                events.Add(new ArcSearchResult(url, name, Deck: null, publisher, IssueCount: 0));
            }
        }

        return events;
    }

    private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReadingListSourceException(DisplayName, $"Comic Book Reading Orders request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsing throws TaskCanceledException, not HttpRequestException.
            throw new ReadingListSourceException(DisplayName, "Comic Book Reading Orders did not respond within 20 seconds.");
        }
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/0.1 (comic library manager)");
        return client;
    }
}
