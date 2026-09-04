using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// readthingsright.com - ported from the real, live-verified
/// <c>_reference/CBLManager/src/CBLManager/ReadThingsRightSource.cs</c>. A curated site covering
/// "eras"/"sagas" (multi-year, multi-writer runs). The hub/index page's arc titles are injected
/// client-side at runtime from <c>hubDicts.js</c>, not present in server-rendered HTML - this
/// adapter fetches that module directly instead of reconstructing the client-side wiring. Issue
/// ranges ("Astro City (1995) #1-6") expand into one <see cref="ArcIssue"/> per number; half-issues
/// ("#1/2") stay singular.
/// </summary>
public sealed class ReadThingsRightSource : IReadingListSource
{
    private const string BaseUrl = "https://www.readthingsright.com/readthingsright";
    private const string HubDictsUrl = BaseUrl + "/hubDicts.js";

    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _throttleLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    private List<ArcSearchResult>? _cachedIndex;
    private readonly Dictionary<string, string> _htmlCache = new();

    public string SourceKey => "ReadThingsRight";
    public string DisplayName => "ReadThingsRight";
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
        string html = await GetCachedHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);
        return ParseIssuesFromHtml(html);
    }

    /// <summary>Pure parsing, split out from the network fetch so it's unit-testable against a literal HTML fixture (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §6).</summary>
    public static List<ArcIssue> ParseIssuesFromHtml(string html)
    {
        var result = new List<ArcIssue>();

        foreach (Match liMatch in Regex.Matches(html, @"<li>(.*?)</li>", RegexOptions.Singleline))
        {
            string text = WebUtility.HtmlDecode(Regex.Replace(liMatch.Groups[1].Value, "<[^>]+>", string.Empty)).Trim();
            text = Regex.Replace(text, @"\s+", " ");

            // Anchored to the entire trimmed line - naturally excludes annotation prose that
            // merely mentions an issue number mid-sentence (real examples exist on live pages).
            var issueMatch = Regex.Match(text, @"^(?<series>.+?)\s#(?<start>\d+(?:/\d+)?)(?:-(?<end>\d+))?$");
            if (!issueMatch.Success)
            {
                continue;
            }

            string seriesRaw = issueMatch.Groups["series"].Value.Trim();
            int year = 0;
            var yearMatch = Regex.Match(seriesRaw, @"^(?<name>.+?)\s*\((?<year>\d{4})\)$");
            string series = seriesRaw;
            if (yearMatch.Success)
            {
                series = yearMatch.Groups["name"].Value.Trim();
                int.TryParse(yearMatch.Groups["year"].Value, out year);
            }

            string startText = issueMatch.Groups["start"].Value;
            if (issueMatch.Groups["end"].Success && !startText.Contains('/'))
            {
                int start = int.Parse(startText);
                int end = int.Parse(issueMatch.Groups["end"].Value);
                for (int n = start; n <= end; n++)
                {
                    result.Add(new ArcIssue(series, n.ToString(), year, CoverImageUrl: null));
                }
            }
            else
            {
                result.Add(new ArcIssue(series, startText, year, CoverImageUrl: null));
            }
        }

        return result;
    }

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        string html = await GetCachedHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);

        // The intro synopsis is the first <p> after the page's <h1>. No cover-art field exists
        // anywhere on this text-only site.
        var descMatch = Regex.Match(html, @"<h1>.*?</h1>\s*<p>(.*?)</p>", RegexOptions.Singleline);
        string? description = descMatch.Success
            ? WebUtility.HtmlDecode(Regex.Replace(descMatch.Groups[1].Value, "<[^>]+>", string.Empty)).Trim()
            : null;
        description = description is null ? null : Regex.Replace(description, @"\s+", " ");

        return new ArcOverviewInfo(description, CoverImageUrl: null);
    }

    private async Task<List<ArcSearchResult>> LoadIndexAsync(CancellationToken cancellationToken)
    {
        string js = await GetHtmlAsync(HubDictsUrl, cancellationToken).ConfigureAwait(false);

        var blockMatch = Regex.Match(js, @"export\s+let\s+comicsDict\s*=\s*\{(.*?)\n\};", RegexOptions.Singleline);
        if (!blockMatch.Success)
        {
            throw new ReadingListSourceException(DisplayName, "Could not locate comicsDict block in hubDicts.js - site markup may have changed.");
        }

        var results = new List<ArcSearchResult>();
        foreach (Match m in Regex.Matches(blockMatch.Groups[1].Value, @"<a href=""\.\./comics/([^""]+)\.html"">([^<]+)</a>"))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            results.Add(new ArcSearchResult(BaseUrl + "/comics/" + slug + ".html", name, Deck: null, Publisher: null, IssueCount: 0));
        }

        return results;
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
            throw new ReadingListSourceException(DisplayName, $"ReadThingsRight request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsing throws TaskCanceledException, not HttpRequestException.
            throw new ReadingListSourceException(DisplayName, "ReadThingsRight did not respond within 20 seconds.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ReadingListSourceException(DisplayName, $"ReadThingsRight returned {(int)response.StatusCode} {response.ReasonPhrase} for {url}");
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
