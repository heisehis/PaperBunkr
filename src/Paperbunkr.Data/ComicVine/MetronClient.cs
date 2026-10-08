using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine;

/// <summary>A comic database Paperbunkr can search, catalog and scrape from: ComicVine or Metron. Everything downstream takes this, so a series works the same with either.</summary>
public interface IComicProvider : IComicVineClient, IComicVineVolumeSearch, IComicVineIssueDetailsSource
{
    ComicProvider Kind { get; }
}

/// <summary>One issue an exact-id lookup found: enough to fetch its details and its series.</summary>
public sealed record ComicIssueHit(int IssueId, int SeriesId);

/// <summary>
/// A source that can find an issue by an id it didn't issue itself (docs/superpowers/specs/2026-10-05-metron-api-efficiency-and-matching-design.md section 2).
/// Metron indexes every issue by its ComicVine id and its barcode, so a book that carries either needs no name search.
/// </summary>
public interface IComicIssueLookup
{
    Task<IReadOnlyList<ComicIssueHit>> FindIssuesByComicVineIdAsync(int comicVineIssueId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ComicIssueHit>> FindIssuesByUpcAsync(string upc, CancellationToken cancellationToken);
}

/// <summary>A source that can say, in one request, which of its series changed since a moment in time (same design, section 1).</summary>
public interface ISeriesChangeSource
{
    /// <summary>Series id to when it last changed, for every series changed after <paramref name="sinceUtc"/>. Null when too much changed to list, which callers treat as "assume all of them".</summary>
    Task<IReadOnlyDictionary<int, DateTime>?> GetSeriesModifiedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken);
}

/// <summary>HTTP plumbing for Metron: one shared, rate-limited client (docs/superpowers/specs/2026-09-20-metron-as-comicvine-alternative-design.md section 3).</summary>
public static class MetronHttp
{
    /// <summary>
    /// Metron's documented limits for a logged-in user are 20 requests a minute (burst) and 5,000 a day. The minute window is enforced here with headroom (18, of which
    /// background work may use 14, so interactive work always has room); a daily overrun surfaces as an HTTP 429, which pauses every caller with the handler's cool-offs.
    /// </summary>
    // No automatic redirects: following one drops the Authorization header, so a mistyped path (a missing trailing slash) reaches Metron as a
    // request with no login - a 401, and three of those in five minutes block the address for a day. A redirect is reported as what it is instead.
    public static ComicVineRateLimitHandler Handler { get; } = new(new HttpClientHandler { AllowAutoRedirect = false }, new ComicVineRateLimitHandler.Options
    {
        MinSpacing = TimeSpan.FromMilliseconds(250),
        Window = TimeSpan.FromMinutes(1),
        HourlyLimit = 18,                 // the option is named for ComicVine's hour; its window is set to a minute here
        LowPriorityLimit = 14,
    });

    public static HttpClient Client { get; } = new(Handler) { Timeout = Timeout.InfiniteTimeSpan };
}

/// <summary>
/// Metron (metron.cloud) as a comic provider, mapped onto the same neutral records the ComicVine client returns. Endpoints and field names are from Metron's own API source:
/// <c>series/?name=</c> (paged, 100 per page), <c>series/{id}/</c>, <c>series/{id}/issue_list/</c>, <c>issue/{id}/</c>; HTTP Basic auth.
/// A Metron series has no cover image; its issues do.
/// </summary>
public sealed class MetronClient : IComicProvider, IPullListSource, Scraping.IStoryArcSource, IComicIssueLookup, ISeriesChangeSource
{
    private const string BaseUrl = "https://metron.cloud/api";
    private const int MaxPages = 30;

    // 100 series a page: past 500 changed series the sweep costs more than it could save for any realistic follow list.
    private const int MaxChangePages = 5;

    private static readonly IReadOnlyDictionary<string, string> RoleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["writer"] = "Writer", ["script"] = "Writer", ["plot"] = "Writer", ["story"] = "Writer",
        ["penciller"] = "Penciller", ["artist"] = "Penciller", ["breakdowns"] = "Penciller",
        ["inker"] = "Inker", ["finishes"] = "Inker",
        ["colorist"] = "Colorist", ["colourist"] = "Colorist",
        ["letterer"] = "Letterer",
        ["cover"] = "CoverArtist", ["cover artist"] = "CoverArtist",
        ["editor"] = "Editor", ["editor in chief"] = "Editor", ["assistant editor"] = "Editor",
        // Added docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md - a confirmed gap:
        // Paperbunkr already has an Issue.Translator field, but this map dropped the role to
        // field = null (see the "Translator" fixture in MetronClientTests.cs).
        ["translator"] = "Translator",
    };

    private static readonly Regex TrailingYear = new(@"\s*\((\d{4})\)\s*$", RegexOptions.Compiled);
    private static readonly Regex DigitalYear = new(@"\s*\((\d{4})\)\s+Digital$", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly string _authorization;
    private readonly ComicVineRequestPriority _priority;

    public MetronClient(string username, string password, ComicVineRequestPriority priority = ComicVineRequestPriority.High, HttpClient? http = null)
    {
        _authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _priority = priority;
        _http = http ?? MetronHttp.Client;
        GuardLogin = http is null;
    }

    /// <summary>
    /// Whether <see cref="MetronLoginGuard"/> applies: yes for the real, shared client; no for a client a test hands in, so one test's
    /// scripted 401 can't hold up the next. A test of the guard itself turns it on.
    /// </summary>
    internal bool GuardLogin { get; init; }

    public ComicProvider Kind => ComicProvider.Metron;

    public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) =>
        SearchVolumesAsync(query, 25, cancellationToken);

    public async Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var volumes = new List<ComicVineVolume>();
        string? url = $"{BaseUrl}/series/?name={Uri.EscapeDataString(query)}";

        for (int page = 0; page < MaxPages && url is not null && volumes.Count < maxResults; page++)
        {
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in (root["results"] as JsonArray) ?? new JsonArray())
            {
                if (node is JsonObject o && ParseSeries(o) is { } volume)
                {
                    volumes.Add(volume);
                }
            }

            url = root["next"]?.GetValue<string?>();
        }

        // Metron doesn't order by size, and ComicVine's order is "most issues first", which callers rely on for tie-breaks.
        return volumes.OrderByDescending(v => v.CountOfIssues).Take(maxResults).ToList();
    }

    public async Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken)
    {
        try
        {
            var root = await GetAsync($"{BaseUrl}/series/{volumeId}/", cancellationToken).ConfigureAwait(false);
            return root is JsonObject o ? ParseSeries(o) : null;
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            return null;
        }
    }

    /// <summary>
    /// A series' <c>associated</c> field (spinoffs/companion series, docs/superpowers/specs/2026-09-23-
    /// metron-api-utilization-design.md) - a separate call from <see cref="GetVolumeAsync"/> rather than
    /// added to the shared <see cref="ComicVineVolume"/> record, since that type is used broadly (search
    /// results, ComicVine parity) and this is Metron-only, series-detail-only data most callers never need.
    /// </summary>
    public async Task<IReadOnlyList<ComicVineIdName>> GetAssociatedSeriesAsync(int seriesId, CancellationToken cancellationToken)
    {
        JsonNode root;
        try
        {
            root = await GetAsync($"{BaseUrl}/series/{seriesId}/", cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            return Array.Empty<ComicVineIdName>();
        }

        // Mokkari's AssociatedSeries schema aliases the name field to "series" (not "name" like every
        // other {id, name} object this client parses) - unconfirmed against a live response, so both
        // keys are tried rather than assuming one.
        if (root is not JsonObject obj || obj["associated"] is not JsonArray items)
        {
            return Array.Empty<ComicVineIdName>();
        }

        return items.OfType<JsonObject>()
            .Select(i => new ComicVineIdName(
                i["id"] is { } id && int.TryParse(id.ToString(), out int parsed) ? parsed : null,
                i["series"]?.GetValue<string>() ?? i["name"]?.GetValue<string>() ?? string.Empty))
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .ToList();
    }

    /// <summary>
    /// A story arc's issues with their dates, for reading-order positions (the same fields the series issue list
    /// above reads). Metron lists an arc's issues at <c>/arc/{id}/issue_list/</c> - written from Metron's
    /// documented API shape, not yet checked against a live response, so a failure or an unexpected shape just
    /// leaves the arc-order fields empty (the resolver treats a <see cref="ComicVineException"/> as "unknown").
    /// </summary>
    public async Task<IReadOnlyList<Scraping.StoryArcIssue>> GetStoryArcIssuesAsync(int storyArcId, CancellationToken cancellationToken)
    {
        var issues = new List<Scraping.StoryArcIssue>();
        string? url = $"{BaseUrl}/arc/{storyArcId}/issue_list/";

        for (int page = 0; page < MaxPages && url is not null; page++)
        {
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in (root["results"] as JsonArray) ?? new JsonArray())
            {
                if (node is JsonObject o && o["id"] is { } idNode && int.TryParse(idNode.ToString(), out int issueId))
                {
                    issues.Add(new Scraping.StoryArcIssue(issueId, o["number"]?.GetValue<string>(), o["store_date"]?.GetValue<string>(), o["cover_date"]?.GetValue<string>()));
                }
            }

            url = root["next"]?.GetValue<string?>();
        }

        return issues;
    }

    public async Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
    {
        var issues = new List<ComicVineIssue>();
        string? url = $"{BaseUrl}/series/{volumeId}/issue_list/";

        for (int page = 0; page < MaxPages && url is not null; page++)
        {
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in (root["results"] as JsonArray) ?? new JsonArray())
            {
                if (node is not JsonObject o || o["id"] is null)
                {
                    continue;
                }

                issues.Add(new ComicVineIssue(
                    o["id"]!.GetValue<int>(),
                    o["number"]?.GetValue<string>() ?? string.Empty,
                    Name: null,
                    StoreDate: ParseDate(o["store_date"]?.GetValue<string>()),
                    CoverDate: ParseDate(o["cover_date"]?.GetValue<string>()),
                    ImageUrl: o["image"]?.GetValue<string>(),
                    VolumeId: volumeId,
                    CoverHash: o["cover_hash"]?.GetValue<string>()));
            }

            url = root["next"]?.GetValue<string?>();
        }

        return issues;
    }

    /// <summary>
    /// Metron bumps a series' <c>modified</c> whenever one of its issues is added, edited or removed, so this one list stands in for re-reading every
    /// followed series' issue list. A long gap can change more series than are worth paging through; past the cap the answer is "unknown" (null).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, DateTime>?> GetSeriesModifiedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var changed = new Dictionary<int, DateTime>();
        string? url = $"{BaseUrl}/series/?modified_gt={Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}";

        for (int page = 0; page < MaxChangePages && url is not null; page++)
        {
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in (root["results"] as JsonArray) ?? new JsonArray())
            {
                if (node is JsonObject o && PositiveInt(o["id"]) is int id)
                {
                    // A row with no readable timestamp still counts as changed: "now" is after any last refresh.
                    changed[id] = ParseDate(o["modified"]?.GetValue<string>()) ?? DateTime.UtcNow;
                }
            }

            url = root["next"]?.GetValue<string?>();
        }

        return url is null ? changed : null;
    }

    public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByComicVineIdAsync(int comicVineIssueId, CancellationToken cancellationToken) =>
        FindIssuesAsync($"cv_id={comicVineIssueId.ToString(CultureInfo.InvariantCulture)}", cancellationToken);

    public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByUpcAsync(string upc, CancellationToken cancellationToken) =>
        FindIssuesAsync($"upc={Uri.EscapeDataString(upc.Trim())}", cancellationToken);

    /// <summary>One page is all an exact filter can need: a caller only acts on exactly one hit, and anything more is "ambiguous" whatever the count.</summary>
    private async Task<IReadOnlyList<ComicIssueHit>> FindIssuesAsync(string filter, CancellationToken cancellationToken)
    {
        var root = await GetAsync($"{BaseUrl}/issue/?{filter}", cancellationToken).ConfigureAwait(false);
        var hits = new List<ComicIssueHit>();
        foreach (var node in (root["results"] as JsonArray) ?? new JsonArray())
        {
            if (node is JsonObject o && PositiveInt(o["id"]) is int issueId && PositiveInt((o["series"] as JsonObject)?["id"]) is int seriesId)
            {
                hits.Add(new ComicIssueHit(issueId, seriesId));
            }
        }

        return hits;
    }

    public async Task<IReadOnlyList<PullListEntry>> GetReleasesAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var entries = new List<PullListEntry>();
        string? url = $"{BaseUrl}/issue/?store_date_range_after={from:yyyy-MM-dd}&store_date_range_before={to:yyyy-MM-dd}";

        for (int page = 0; page < MaxPages && url is not null; page++)
        {
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in (root["results"] as JsonArray) ?? new JsonArray())
            {
                if (node is not JsonObject o || o["id"] is null || o["series"] is not JsonObject series || series["id"] is null
                    || ParseDate(o["store_date"]?.GetValue<string>()) is not DateTime storeDate)
                {
                    continue;
                }

                entries.Add(new PullListEntry(
                    o["id"]!.GetValue<int>(),
                    series["id"]!.GetValue<int>(),
                    (series["name"]?.GetValue<string>() ?? string.Empty).Trim(),
                    o["number"]?.GetValue<string>() ?? string.Empty,
                    storeDate,
                    ParseDate(o["cover_date"]?.GetValue<string>()),
                    o["image"]?.GetValue<string>(),
                    ParseDate(o["foc_date"]?.GetValue<string>())));
            }

            url = root["next"]?.GetValue<string?>();
        }

        return entries;
    }

    public async Task<PullListSeriesInfo?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
    {
        JsonNode root;
        try
        {
            root = await GetAsync($"{BaseUrl}/series/{seriesId}/", cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            return null;
        }

        if (root is not JsonObject o)
        {
            return null;
        }

        int? year = o["year_began"] is { } y && int.TryParse(y.ToString(), out int parsedYear) ? parsedYear : null;
        int? cvId = o["cv_id"] is { } c && int.TryParse(c.ToString(), out int parsedCv) && parsedCv > 0 ? parsedCv : null;
        // gcd_id: every Metron record carries the Grand Comics Database id (docs/superpowers/specs/2026-09-27-gcd-data-design.md §3).
        return new PullListSeriesInfo(seriesId, CleanName(o["name"]?.GetValue<string>() ?? string.Empty, year), (o["publisher"] as JsonObject)?["name"]?.GetValue<string>(), year, cvId, PositiveInt(o["gcd_id"]));
    }

    public async Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken)
    {
        JsonNode root;
        try
        {
            root = await GetAsync($"{BaseUrl}/issue/{issueId}/", cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            return null;
        }

        if (root is not JsonObject o)
        {
            return null;
        }

        var series = o["series"] as JsonObject;
        var credits = new List<ComicVineCredit>();
        foreach (var credit in (o["credits"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var name = credit["creator"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string? field = null;
            foreach (var role in (credit["role"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var roleName = role["name"]?.GetValue<string>();
                int? roleId = role["id"] is { } r && int.TryParse(r.ToString(), out int parsedRoleId) ? parsedRoleId : null;
                if (roleName is not null && RoleMap.TryGetValue(roleName.Trim(), out var mapped))
                {
                    // One credit can carry several roles (writer and artist); each is its own credit line so every field is filled.
                    // Metron's credit objects carry no separate creator id, only a role id (confirmed during design) - CreatorExternalId stays null here.
                    credits.Add(new ComicVineCredit(name, mapped, RoleExternalId: roleId));
                    field ??= mapped;
                }
            }

            if (field is null)
            {
                credits.Add(new ComicVineCredit(name, null));
            }
        }

        var cover = ParseDatePart(o["cover_date"]?.GetValue<string>());
        var store = ParseDatePart(o["store_date"]?.GetValue<string>());
        // "name" is a list of story titles; a single-story issue's title is what a reader means by the issue's title.
        var storyTitles = (o["name"] as JsonArray)?.Select(n => n?.GetValue<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string?>();
        var title = o["title"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = storyTitles.Count > 0 ? string.Join(" / ", storyTitles) : null;
        }

        // Genre rides along on the nested series object embedded in the issue-detail response -
        // confirmed present there (mokkari's IssueSeries schema: id, name, sort_name, alt_names,
        // volume, year_began, series_type, genres, language). Status is NOT on this nested object
        // (confirmed absent from IssueSeries - it only exists on the standalone /series/{id}/ detail
        // response) - an earlier version of this parser read series?["status"] here, which mokkari's
        // real schema shows can never be populated from this endpoint; removed rather than left as a
        // silent no-op that looked like working code (docs/superpowers/specs/2026-09-23-metron-api-
        // utilization-design.md).
        var genres = (series?["genres"] as JsonArray)?.OfType<JsonObject>()
            .Select(g => g["name"]?.GetValue<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList() ?? new List<string>();

        var variants = (o["variants"] as JsonArray)?.OfType<JsonObject>()
            .Where(v => v["image"]?.GetValue<string>() is { Length: > 0 })
            .Select(v => new ComicVineVariantCover(v["name"]?.GetValue<string>(), v["image"]!.GetValue<string>()))
            .ToList() ?? new List<ComicVineVariantCover>();

        return new ComicVineIssueDetails(
            o["id"]?.GetValue<int>() ?? issueId,
            series?["id"]?.GetValue<int>() ?? 0,
            series?["name"]?.GetValue<string>(),
            o["number"]?.GetValue<string>(),
            title,
            o["resource_url"]?.GetValue<string>(),
            cover,
            store,
            StripHtml(o["desc"]?.GetValue<string>()),
            IdNames(o["arcs"]),
            IdNames(o["characters"]),
            IdNames(o["teams"]),
            Array.Empty<ComicVineIdName>(),      // Metron has no locations (confirmed - no such REST resource)
            credits,
            Universes: IdNames(o["universes"]),
            AgeRating: (o["rating"] as JsonObject)?["name"]?.GetValue<string>(),
            Isbn: o["isbn"]?.GetValue<string>(),
            Upc: o["upc"]?.GetValue<string>(),
            Genres: genres,
            AverageRating: o["average_rating"] is { } avgNode && double.TryParse(avgNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double avg) ? avg : null,
            RatingCount: o["rating_count"] is { } rc && int.TryParse(rc.ToString(), out int count) ? count : null,
            Imprint: (o["imprint"] as JsonObject)?["name"]?.GetValue<string>(),
            Variants: variants,
            GcdId: PositiveInt(o["gcd_id"]));
    }

    /// <summary>The <c>detail</c> text of a Metron error body, trimmed of its trailing full stop, or null when there is none.</summary>
    private static string? Detail(string body)
    {
        try
        {
            string? detail = (JsonNode.Parse(body) as JsonObject)?["detail"]?.GetValue<string>()?.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(detail) ? null : detail;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int? PositiveInt(JsonNode? node) => node is not null && int.TryParse(node.ToString(), out int value) && value > 0 ? value : null;

    private static IReadOnlyList<ComicVineIdName> IdNames(JsonNode? array) =>
        array is JsonArray items
            ? items.OfType<JsonObject>()
                .Where(i => i["name"]?.GetValue<string>() is { Length: > 0 })
                .Select(i => new ComicVineIdName(
                    i["id"] is { } id && int.TryParse(id.ToString(), out int parsed) ? parsed : null,
                    i["name"]!.GetValue<string>()))
                .ToList()
            : new List<ComicVineIdName>();

    /// <summary>Metron's list item shows the name with the year appended (<c>Name (2018)</c>); the picker shows the year itself, so it is stripped when it agrees with <c>year_began</c>.</summary>
    private static ComicVineVolume? ParseSeries(JsonObject o)
    {
        if (o["id"] is null)
        {
            return null;
        }

        int? year = o["year_began"] is { } y ? (int.TryParse(y.ToString(), out int parsed) ? parsed : null) : null;
        var raw = o["series"]?.GetValue<string>() ?? o["name"]?.GetValue<string>() ?? string.Empty;
        var name = CleanName(raw, year);
        int count = o["issue_count"] is { } c && int.TryParse(c.ToString(), out int n) ? n : 0;

        return new ComicVineVolume(o["id"]!.GetValue<int>(), name, (o["publisher"] as JsonObject)?["name"]?.GetValue<string>(), year, count, ImageUrl: null);
    }

    public static string CleanName(string display, int? year)
    {
        if (year is int y)
        {
            var digital = DigitalYear.Match(display);
            if (digital.Success && digital.Groups[1].Value == y.ToString(CultureInfo.InvariantCulture))
            {
                return DigitalYear.Replace(display, " Digital").Trim();
            }

            var trailing = TrailingYear.Match(display);
            if (trailing.Success && trailing.Groups[1].Value == y.ToString(CultureInfo.InvariantCulture))
            {
                return TrailingYear.Replace(display, string.Empty).Trim();
            }
        }

        return display.Trim();
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    private static ComicVineDatePart ParseDatePart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ComicVineDatePart(null, null, null);
        }

        var segments = text.Split('-', StringSplitOptions.TrimEntries);
        int? year = segments.Length > 0 && int.TryParse(segments[0], out int y) ? y : null;
        int? month = segments.Length > 1 && int.TryParse(segments[1], out int m) ? m : null;
        int? day = segments.Length > 2 && int.TryParse(segments[2].Split(' ')[0], out int d) ? d : null;
        return new ComicVineDatePart(year, month, day);
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        var decoded = WebUtility.HtmlDecode(Tags.Replace(html, " "));
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    /// <summary>CE's real <c>__get_dom</c> retries the whole request exactly once, after a flat 2.5s
    /// sleep, on ANY transport/parse-level failure (cvconnection.py:159-219, verified directly against
    /// source). Docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.2 extends this
    /// to Metron too. Deliberately NOT retried: a well-formed HTTP response carrying a real API-level
    /// rejection (401/403/404/429/other 4xx/5xx) - that's an answer, not a failure to get one.</summary>
    /// <summary>Mutable (not <c>readonly</c>) so tests can shrink it - a real 2.5s sleep per retried
    /// test would make the suite slow for no benefit, same test-seam shape as <see cref="MetronQuota.Clock"/>.</summary>
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(2500);

    private Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, url, body: null, retryTransportFailure: true, cancellationToken);

    /// <summary>
    /// Every request to Metron, reads and (for <see cref="MetronAccountClient"/>) writes alike, so one place owns the login, the quota bookkeeping and the error mapping.
    /// <paramref name="retryTransportFailure"/> is for calls that are safe to repeat: a write whose request may have reached Metron before the connection dropped
    /// must not be sent twice unless doing so changes nothing. A reply with no body (a 204 from a delete) comes back as an empty object.
    /// </summary>
    internal async Task<JsonNode> SendAsync(HttpMethod method, string url, JsonNode? body, bool retryTransportFailure, CancellationToken cancellationToken)
    {
        // Background work leaves the last slice of the day's quota for interactive use; it waits for the reset instead.
        if (_priority == ComicVineRequestPriority.Low && MetronQuota.BackgroundShouldWait(out var untilReset))
        {
            throw new ComicVineException($"Metron's daily limit is nearly used up, so background updates resume in about {Math.Max(1, (int)Math.Ceiling(untilReset.TotalMinutes))} minutes.", 107);
        }

        // A login Metron has just rejected is not sent again: three 401s in five minutes get the address blocked for a day.
        if (GuardLogin && MetronLoginGuard.ShouldHold(_authorization, out var hold))
        {
            throw new ComicVineException(MetronLoginGuard.HoldMessage(hold), 100);
        }

        JsonNode? root = null;
        ComicVineException? lastFailure = null;

        int attempts = retryTransportFailure ? 2 : 1;
        for (int attempt = 0; attempt < attempts && root is null; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            string responseBody;
            HttpStatusCode status;
            try
            {
                using var request = new HttpRequestMessage(method, url);
                request.Options.Set(ComicVineRateLimitHandler.PriorityKey, _priority);
                if (body is not null)
                {
                    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authorization);
                request.Headers.UserAgent.ParseAdd("Paperbunkr (comic library manager)");
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                MetronQuota.Observe(response.Headers);
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                lastFailure = new ComicVineException($"Metron request failed: {ex.Message}", inner: ex);
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new ComicVineException("Metron did not respond in time.");
                continue;
            }

            // The status codes reuse ComicVine's numbering so callers treat both providers alike: 100 =
            // the credentials were refused, 101 = not found, 107 = rate limited. A well-formed 4xx
            // client-error rejection is a real API answer, not a transport failure - it throws
            // immediately, no retry. A 5xx server error, by contrast, is exactly the kind of transient
            // failure the retry-once exists for (a server hiccup, not a stable answer) - it falls
            // through to the retry loop instead of throwing directly.
            switch (status)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    if (GuardLogin)
                    {
                        MetronLoginGuard.RecordRejected(_authorization);
                    }

                    // Metron says why in its own words ("Invalid username/password.", "You do not have permission..."); a 403 is not a wrong
                    // password, and saying "rejected your login" for both sent people to retype a password that was fine.
                    string? detail = Detail(responseBody);
                    throw new ComicVineException(status == HttpStatusCode.Unauthorized
                        ? $"Metron rejected your login{(detail is null ? "" : $" ({detail})")}. Check it under Preferences → Connections."
                        : $"Metron refused this request{(detail is null ? "" : $" ({detail})")}. Your login was accepted, but it isn't allowed to do this.", 100)
                    {
                        HttpStatus = (int)status,
                    };
                case HttpStatusCode.NotFound:
                    throw new ComicVineException("Metron has no such record.", 101);
                case HttpStatusCode.TooManyRequests:
                    throw new ComicVineException("Metron's rate limit was reached; try again in a minute.", 107);
            }

            if ((int)status >= 500)
            {
                lastFailure = new ComicVineException($"Metron returned HTTP {(int)status}.");
                continue;
            }

            if ((int)status is >= 300 and < 400)
            {
                throw new ComicVineException($"Metron answered with a redirect (HTTP {(int)status}) instead of data for {new Uri(url).AbsolutePath}. This is a fault in Paperbunkr, not in your login.") { HttpStatus = (int)status };
            }

            if ((int)status >= 400)
            {
                throw new ComicVineException($"Metron returned HTTP {(int)status}.") { HttpStatus = (int)status };
            }

            if (method != HttpMethod.Get && string.IsNullOrWhiteSpace(responseBody))
            {
                return new JsonObject();
            }

            try
            {
                root = JsonNode.Parse(responseBody);
                if (root is null)
                {
                    lastFailure = new ComicVineException("Metron returned an empty response.");
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                lastFailure = new ComicVineException($"Metron returned an unexpected response: {ex.Message}", inner: ex);
            }
        }

        return root ?? throw lastFailure!;
    }
}
