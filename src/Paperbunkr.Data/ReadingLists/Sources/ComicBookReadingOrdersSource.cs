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
public sealed class ComicBookReadingOrdersSource : IReadingListSource, Paperbunkr.Data.Metadata.ICbroTimeline
{
    private const string BaseUrl = "https://comicbookreadingorders.com";

    private static readonly string[] EventTimelineUrls =
    {
        BaseUrl + "/dc/event-timeline/",
        BaseUrl + "/marvel/event-timeline/",
    };

    private static readonly HttpClient Http = CreateClient();

    private const string OverlapNotePhrases = @"takes\s+place\s+during|takes\s+place\s+simultaneously\s+with|occurs\s+during\s+the\s+same\s+time\s+as";

    private static readonly Regex OverlapNotePattern = new(
        $@"(?<original>(?:{OverlapNotePhrases})\s+(?<target>.+?)(?:\s+\((?<year>(?:19|20)\d{{2}})\))?[.!?]?(?=\s*(?:{OverlapNotePhrases}|\z)))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The site's robots.txt asks for "crawl-delay: 5" (checked 2026-10-12); a 500 ms pace was well under that.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(5);

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

    /// <summary>The Marvel and DC "Event Order" lists in page order. That order is the site's story order (the same order as its Master Reading Order), not strict publication order.</summary>
    public async Task<IReadOnlyList<Paperbunkr.Data.Metadata.CbroTimelineEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        var entries = new List<Paperbunkr.Data.Metadata.CbroTimelineEntry>();
        foreach (string indexUrl in EventTimelineUrls)
        {
            string html = await GetHtmlAsync(indexUrl, cancellationToken).ConfigureAwait(false);
            entries.AddRange(ParseTimeline(html, indexUrl.Contains("/dc/", StringComparison.Ordinal) ? "dc" : "marvel"));
        }

        return entries;
    }

    /// <summary>
    /// Pure parsing of one event-timeline page. Entries are the <c>&lt;p&gt;</c> paragraphs after the "Event Order" heading that read
    /// "Name (Year)" - linked ones carry the event's own page, older ones are plain text with the issues in a blue span. Era markers
    /// ("Marvel NOW! starts here.") have no year and are skipped.
    /// </summary>
    public static List<Paperbunkr.Data.Metadata.CbroTimelineEntry> ParseTimeline(string html, string listKey)
    {
        int heading = html.IndexOf("Event Order", StringComparison.OrdinalIgnoreCase);
        string body = heading >= 0 ? html[heading..] : html;

        var entries = new List<Paperbunkr.Data.Metadata.CbroTimelineEntry>();
        foreach (Match paragraph in Regex.Matches(body, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string inner = paragraph.Groups[1].Value;
            string text = WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", string.Empty)).Replace(' ', ' ').Trim();
            var match = Regex.Match(text, @"^(?<name>.+?)\s*\((?<year>(?:19|20)\d{2})\)");
            if (!match.Success)
            {
                continue;
            }

            string name = match.Groups["name"].Value.Trim().TrimEnd('-', '–', '—').Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var href = Regex.Match(inner, @"href=""(?<url>https://comicbookreadingorders\.com/[^""]*/events/[^""]+)""", RegexOptions.IgnoreCase);
            var notes = OverlapNotePattern.Matches(text).Cast<Match>()
                .Select(noteMatch =>
                {
                    string target = noteMatch.Groups["target"].Value.Trim().TrimEnd('.', '!', '?', ' ', '\u00a0');
                    return target.Length == 0 ? null : new Paperbunkr.Data.Metadata.CbroTimelineOverlapNote(
                        noteMatch.Groups["original"].Value.Trim(), target,
                        int.TryParse(noteMatch.Groups["year"].Value, out int targetYear) ? targetYear : null);
                })
                .OfType<Paperbunkr.Data.Metadata.CbroTimelineOverlapNote>()
                .ToList();
            entries.Add(new Paperbunkr.Data.Metadata.CbroTimelineEntry(
                listKey, entries.Count + 1, name, int.Parse(match.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture),
                href.Success ? WebUtility.HtmlDecode(href.Groups["url"].Value) : null, notes));
        }

        return entries;
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        // arcId is the arc's own page URL - simplest stable identifier this site offers, since it
        // has no numeric resource ids anywhere.
        string html = await GetHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);
        return ParseIssuesFromHtml(html);
    }

    /// <summary>The same page, keeping the lines that look like an entry but could not be read as "Series #number" (a one-shot, a Free Comic
    /// Book Day issue), so they are reported instead of silently left out (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md
    /// §5.7, decision Q13).</summary>
    public async Task<ArcEntryList> GetArcEntriesAsync(string arcId, CancellationToken cancellationToken)
    {
        string html = await GetHtmlAsync(arcId, cancellationToken).ConfigureAwait(false);
        return ParseEntriesFromHtml(html);
    }

    /// <summary>Pure parsing, split out from the network fetch so it's unit-testable against a literal HTML fixture (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §6). Handles both real markup styles this site uses (per-issue &lt;p&gt; wrapping, and bare &lt;span&gt; runs with no wrapper).</summary>
    public static List<ArcIssue> ParseIssuesFromHtml(string html) => ParseEntriesFromHtml(html).Issues.ToList();

    /// <summary><see cref="ParseIssuesFromHtml"/> plus the skipped lines: a line between the first and the last issue that ends in a "(year)"
    /// like every entry does, yet has no "#number". No number is guessed for it.</summary>
    public static ArcEntryList ParseEntriesFromHtml(string html)
    {
        // The site's markup for the issue list is NOT consistent across arcs - some pages wrap
        // every issue in its own <p>issue</p>, others just run <span style="color:...">issue</span>
        // (year)<br/> sequences with no per-issue <p> wrapper at all. A tag-agnostic, line-based
        // approach handles both: normalize every plausible boundary into a newline, strip
        // remaining tags, treat each resulting line as one candidate issue entry.
        html = Regex.Replace(html, @"<style[^>]*>.*?</style>", string.Empty, RegexOptions.Singleline);
        html = Regex.Replace(html, @"<script[^>]*>.*?</script>", string.Empty, RegexOptions.Singleline);

        // A blue span is a comment/annotation ("Takes place during X"), never an issue. It is kept as a marker line: it describes the
        // issue that FOLLOWS it, and role detection reads it ("Tie-In", "Prelude"). Section headings (h2-h6; h1 is the page title, which
        // would label the whole arc) are kept the same way and apply to every issue until the next heading.
        html = Regex.Replace(html, @"<span style=""color:\s*#0000ff;?"">(.*?)</span>", m => "\n" + NoteMarker + StripTags(m.Groups[1].Value) + "\n", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<h[2-6][^>]*>(.*?)</h[2-6]>", m => "\n" + HeadingMarker + StripTags(m.Groups[1].Value) + "\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Every boundary becomes a newline. (This used to substitute an unclosed `<span style="color:` for `</p>`/`<br>`, which the tag
        // strip below then closed at the NEXT `>` - swallowing any text between two such boundaries.)
        html = Regex.Replace(html, @"</p>|<br\s*/?>|</h[1-6]>|</strong>|</b>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<span style=""color:", "\n<span style=\"color:", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", string.Empty);

        var result = new List<ArcIssue>();
        var unread = new List<(int After, string Text)>();
        string? section = null;
        string? pendingNote = null;
        foreach (string line in html.Split('\n'))
        {
            string text = WebUtility.HtmlDecode(line).Trim();
            if (text.StartsWith(HeadingMarker, StringComparison.Ordinal))
            {
                section = text[HeadingMarker.Length..].Trim();
                continue;
            }

            if (text.StartsWith(NoteMarker, StringComparison.Ordinal))
            {
                pendingNote = text[NoteMarker.Length..].Trim();
                continue;
            }

            var issueMatch = Regex.Match(text, @"^(?<series>.+?)\s+#(?<number>\d+[A-Za-z]?)\b(?<rest>.*)$");
            if (!issueMatch.Success)
            {
                if (text.Length is > 0 and <= 120 && Regex.IsMatch(text, @"\(\d{4}\)\s*$"))
                {
                    unread.Add((result.Count, text));
                }

                continue;
            }

            int year = 0;
            var yearMatch = Regex.Match(issueMatch.Groups["rest"].Value, @"\((\d{4})\)");
            if (yearMatch.Success)
            {
                int.TryParse(yearMatch.Groups[1].Value, out year);
            }

            string? annotation = string.Join(" | ", new[] { section, pendingNote }.Where(part => !string.IsNullOrWhiteSpace(part)));
            pendingNote = null;
            result.Add(new ArcIssue(issueMatch.Groups["series"].Value.Trim(), issueMatch.Groups["number"].Value, year, CoverImageUrl: null,
                Annotation: annotation.Length == 0 ? null : annotation));
        }

        // Only lines inside the issue list count: the page around it has headings and credits that also end in a year.
        var skipped = unread.Where(u => u.After > 0 && u.After < result.Count).Select(u => u.Text).ToList();
        return new ArcEntryList(result, skipped);
    }

    private const string NoteMarker = "@@NOTE@@";

    private const string HeadingMarker = "@@HEAD@@";

    private static string StripTags(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty)).Trim();

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
