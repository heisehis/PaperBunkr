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

/// <summary>HTTP plumbing for Metron: one shared, rate-limited client (docs/superpowers/specs/2026-09-20-metron-as-comicvine-alternative-design.md section 3).</summary>
public static class MetronHttp
{
    /// <summary>
    /// Metron's documented limits for a logged-in user are 20 requests a minute (burst) and 5,000 a day. The minute window is enforced here with headroom (18, of which
    /// background work may use 14, so interactive work always has room); a daily overrun surfaces as an HTTP 429, which pauses every caller with the handler's cool-offs.
    /// </summary>
    public static ComicVineRateLimitHandler Handler { get; } = new(new HttpClientHandler(), new ComicVineRateLimitHandler.Options
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
public sealed class MetronClient : IComicProvider, IPullListSource, Scraping.IStoryArcSource
{
    private const string BaseUrl = "https://metron.cloud/api";
    private const int MaxPages = 30;

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
    }

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
                    VolumeId: volumeId));
            }

            url = root["next"]?.GetValue<string?>();
        }

        return issues;
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

    private async Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken)
    {
        // Background work leaves the last slice of the day's quota for interactive use; it waits for the reset instead.
        if (_priority == ComicVineRequestPriority.Low && MetronQuota.BackgroundShouldWait(out var untilReset))
        {
            throw new ComicVineException($"Metron's daily limit is nearly used up, so background updates resume in about {Math.Max(1, (int)Math.Ceiling(untilReset.TotalMinutes))} minutes.", 107);
        }

        JsonNode? root = null;
        ComicVineException? lastFailure = null;

        for (int attempt = 0; attempt < 2 && root is null; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            string body;
            HttpStatusCode status;
            try
            {
                using var request = ComicVineHttp.Get(url, _priority);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authorization);
                request.Headers.UserAgent.ParseAdd("Paperbunkr (comic library manager)");
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                MetronQuota.Observe(response.Headers);
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
                    throw new ComicVineException("Metron rejected your login. Check it under Preferences → Connections.", 100);
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

            if ((int)status >= 400)
            {
                throw new ComicVineException($"Metron returned HTTP {(int)status}.");
            }

            try
            {
                root = JsonNode.Parse(body);
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
