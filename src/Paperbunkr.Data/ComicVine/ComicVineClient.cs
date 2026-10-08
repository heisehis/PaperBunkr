using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.Data.ComicVine;

public sealed record ComicVineVolume(int Id, string Name, string? Publisher, int? StartYear, int CountOfIssues, string? ImageUrl);

/// <param name="CoverHash">Metron's perceptual hash of the cover (<see cref="Scraping.MetronCoverHash"/>); ComicVine has none.</param>
public sealed record ComicVineIssue(int Id, string IssueNumber, string? Name, DateTime? StoreDate, DateTime? CoverDate, string? ImageUrl, int? VolumeId, string? CoverHash = null);

/// <summary>ComicVine could not be reached, rejected the request, or returned something unparseable.</summary>
public sealed class ComicVineException(string message, int? apiStatusCode = null, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>ComicVine's own <c>status_code</c> when it answered (100 = invalid key, 101 = not found, 107 = rate limit...).</summary>
    public int? ApiStatusCode { get; } = apiStatusCode;

    /// <summary>The HTTP status behind the failure when the server answered with one this class has no <see cref="ApiStatusCode"/> for (a 400 from a write, say).</summary>
    public int? HttpStatus { get; init; }
}

/// <summary>Volume and issue lookups for the acquisition daemon and the "track this series" UI.</summary>
public interface IComicVineClient
{
    Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken);

    /// <summary>Word-based fuzzy series search, as the original ComicVine Scraper does it. Defaults to the plain name-filter search for a source with no such mode (Metron).</summary>
    Task<IReadOnlyList<ComicVineVolume>> SearchVolumesFuzzyAsync(string query, CancellationToken cancellationToken) => SearchVolumesAsync(query, cancellationToken);

    Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken);

    /// <summary>Every issue of a volume, following ComicVine's 100-per-page pagination.</summary>
    Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken);
}

/// <summary>
/// ComicVine REST client for volumes and issues (docs/superpowers/specs/2026-09-19-comic-acquisition-
/// daemon-design.md §3). Field names and the 100-item page limit follow ComicVine's published API
/// documentation. Everything goes through the shared <see cref="ComicVineHttp"/> client (so the global rate
/// limit applies) unless a test injects its own <see cref="HttpClient"/>.
/// <para>
/// Priority: the daemon constructs this with <see cref="ComicVineRequestPriority.Low"/> so its polling yields to
/// the UI; interactive lookups use <see cref="ComicVineRequestPriority.High"/>.
/// </para>
/// </summary>
public sealed class ComicVineClient : IComicProvider, IPullListSource, Scraping.IStoryArcSource
{
    private const string BaseUrl = "https://comicvine.gamespot.com/api";
    private const int PageSize = 100;
    /// <summary>Safety valve against a runaway pagination loop (5,000 issues is far beyond any real volume).</summary>
    private const int MaxPages = 50;

    private const string VolumeFields = "id,name,publisher,start_year,count_of_issues,image";
    private const string IssueFields = "id,issue_number,name,store_date,cover_date,image,volume";

    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly ComicVineRequestPriority _priority;
    private readonly ComicVineRateLimitHandler? _rateLimiter;

    public ComicVineClient(string apiKey, ComicVineRequestPriority priority = ComicVineRequestPriority.High,
        HttpClient? http = null, ComicVineRateLimitHandler? rateLimiter = null)
    {
        _apiKey = apiKey;
        _priority = priority;
        _http = http ?? ComicVineHttp.Client;
        _rateLimiter = rateLimiter ?? (http is null ? ComicVineHttp.Handler : null);
    }

    public async Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken)
    {
        // /volumes/ with a name filter rather than the generic /search/ endpoint: the existing ComicVineSource
        // documents /search/ silently returning empty for some resource types.
        var url = Url("volumes", $"filter=name:{Uri.EscapeDataString(query)}&field_list={VolumeFields}&limit=25&sort=count_of_issues:desc");
        var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        return (root["results"] as JsonArray)?.Select(ParseVolume).OfType<ComicVineVolume>().ToList() ?? new List<ComicVineVolume>();
    }

    /// <summary>
    /// CE's actual series search (cvconnection.py <c>_query_series_ids_dom</c>, verified): the fuzzy
    /// <c>/search/?resources=volume</c> endpoint, up to 100 results. The name-filter search above is a
    /// substring match on the exact stored name - "Batman Dark Victory" never finds "Batman: Dark
    /// Victory" - and its 25-result cap sorted by issue count drops short or recent series entirely.
    /// </summary>
    public async Task<IReadOnlyList<ComicVineVolume>> SearchVolumesFuzzyAsync(string query, CancellationToken cancellationToken)
    {
        var url = Url("search", $"resources=volume&field_list={VolumeFields}&limit={PageSize}&query={Uri.EscapeDataString(query)}");
        var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        return (root["results"] as JsonArray)?.Select(ParseVolume).OfType<ComicVineVolume>().ToList() ?? new List<ComicVineVolume>();
    }

    public async Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        // The single-page overload above stops at 25 by issue count, which surfaces only the longest old runs that share a word with the name. This walks
        // further (ComicVine's page limit is 100) so a short or recent series is in the set at all; callers rank the whole set themselves.
        var volumes = new List<ComicVineVolume>();
        int offset = 0;

        for (int page = 0; page < 10 && volumes.Count < maxResults; page++)
        {
            int limit = Math.Min(PageSize, maxResults - volumes.Count);
            var url = Url("volumes", $"filter=name:{Uri.EscapeDataString(query)}&field_list={VolumeFields}&limit={limit}&offset={offset}&sort=count_of_issues:desc");
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);

            var results = root["results"] as JsonArray;
            if (results is null || results.Count == 0)
            {
                break;
            }

            volumes.AddRange(results.Select(ParseVolume).OfType<ComicVineVolume>());
            offset += results.Count;
            int total = root["number_of_total_results"]?.GetValue<int>() ?? 0;
            if (offset >= total)
            {
                break;
            }
        }

        return volumes;
    }

    public async Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken)
    {
        try
        {
            var root = await GetAsync(Url($"volume/4050-{volumeId}", $"field_list={VolumeFields}"), cancellationToken).ConfigureAwait(false);
            return root["results"] is JsonObject volume ? ParseVolume(volume) : null;
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            return null; // "Object Not Found"
        }
    }

    public async Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
    {
        var issues = new List<ComicVineIssue>();
        int offset = 0;

        for (int page = 0; page < MaxPages; page++)
        {
            var url = Url("issues", $"filter=volume:{volumeId}&field_list={IssueFields}&limit={PageSize}&offset={offset}&sort=cover_date:asc");
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);

            var results = root["results"] as JsonArray;
            if (results is null || results.Count == 0)
            {
                break;
            }

            issues.AddRange(results.Select(ParseIssue).OfType<ComicVineIssue>());

            offset += results.Count;
            int total = root["number_of_total_results"]?.GetValue<int>() ?? 0;
            if (offset >= total)
            {
                break;
            }
        }

        return issues;
    }

    /// <summary>
    /// Every issue with a store date in the window, across all volumes (<c>issues?filter=store_date:from|to</c>, 100 a page). It costs one request per 100 issues from ComicVine's
    /// small hourly budget, so it is the fallback when no Metron login is saved, not the first choice.
    /// </summary>
    public async Task<IReadOnlyList<PullListEntry>> GetReleasesAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var entries = new List<PullListEntry>();
        int offset = 0;

        for (int page = 0; page < MaxPages; page++)
        {
            var url = Url("issues", $"filter=store_date:{from:yyyy-MM-dd}|{to:yyyy-MM-dd}&field_list={IssueFields}&limit={PageSize}&offset={offset}&sort=store_date:asc");
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);

            var results = root["results"] as JsonArray;
            if (results is null || results.Count == 0)
            {
                break;
            }

            foreach (var node in results)
            {
                if (node is not JsonObject o || o["id"] is null || o["volume"] is not JsonObject volume || volume["id"] is null || ParseDate(o["store_date"]) is not DateTime storeDate)
                {
                    continue;
                }

                entries.Add(new PullListEntry(
                    o["id"]!.GetValue<int>(),
                    volume["id"]!.GetValue<int>(),
                    (volume["name"]?.GetValue<string>() ?? string.Empty).Trim(),
                    o["issue_number"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    storeDate,
                    ParseDate(o["cover_date"]),
                    ImageUrl(o["image"])));
            }

            offset += results.Count;
            int total = root["number_of_total_results"]?.GetValue<int>() ?? 0;
            if (offset >= total)
            {
                break;
            }
        }

        return entries;
    }

    public async Task<PullListSeriesInfo?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
    {
        var volume = await GetVolumeAsync(seriesId, cancellationToken).ConfigureAwait(false);
        return volume is null ? null : new PullListSeriesInfo(volume.Id, volume.Name, volume.Publisher, volume.StartYear, ComicVineId: null);
    }

    /// <summary>
    /// ComicVine's own person-role -> Paperbunkr credit field mapping (CE's real <c>cvdb.py</c>
    /// <c>ROLE_DICT</c>, verified directly against the extracted plugin source - docs/superpowers/specs/
    /// 2026-09-24-comicvine-scraper-fidelity-design.md §2.6). "Artist" fans out to BOTH Penciller and
    /// Inker in CE (cvdb.py:663-667: <c>artist -> [pencillers_sl, inkers_sl]</c>) - every other key maps
    /// to exactly one field, but the value type has to be an array to express this one case honestly.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> PersonRoleMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["writer"] = new[] { "Writer" },
        ["penciler"] = new[] { "Penciller" },
        ["penciller"] = new[] { "Penciller" },
        ["artist"] = new[] { "Penciller", "Inker" },
        ["inker"] = new[] { "Inker" },
        ["cover"] = new[] { "CoverArtist" },
        ["editor"] = new[] { "Editor" },
        ["colorer"] = new[] { "Colorist" },
        ["colorist"] = new[] { "Colorist" },
        ["letterer"] = new[] { "Letterer" },
    };

    private const string IssueDetailFields = "id,name,issue_number,site_detail_url,cover_date,store_date,description,volume,story_arc_credits,character_credits,team_credits,location_credits,concept_credits,person_credits";

    /// <summary>
    /// The fork's <c>_query_story_arc_order</c> inputs: the arc's issue ids (<c>story_arc/4045-{id}</c>, 4045- being
    /// ComicVine's story-arc prefix), then issue number and both dates for those ids in batches of 100 (its page limit).
    /// </summary>
    public async Task<IReadOnlyList<Scraping.StoryArcIssue>> GetStoryArcIssuesAsync(int storyArcId, CancellationToken cancellationToken)
    {
        var arc = await GetAsync(Url($"story_arc/4045-{storyArcId}", "field_list=id,name,issues"), cancellationToken).ConfigureAwait(false);
        var ids = ((arc["results"] as JsonObject)?["issues"] as JsonArray)?
            .OfType<JsonObject>()
            .Select(i => i["id"] is { } id && int.TryParse(id.ToString(), out int parsed) ? parsed : (int?)null)
            .OfType<int>()
            .ToList() ?? new List<int>();

        var issues = new List<Scraping.StoryArcIssue>();
        for (int i = 0; i < ids.Count; i += PageSize)
        {
            string filter = string.Join('|', ids.Skip(i).Take(PageSize));
            var page = await GetAsync(Url("issues", $"field_list=id,issue_number,store_date,cover_date&limit={PageSize}&filter=id:{filter}"), cancellationToken).ConfigureAwait(false);
            foreach (var node in (page["results"] as JsonArray) ?? new JsonArray())
            {
                if (node is JsonObject o && o["id"] is { } idNode && int.TryParse(idNode.ToString(), out int issueId))
                {
                    issues.Add(new Scraping.StoryArcIssue(issueId, o["issue_number"]?.GetValue<string>(), o["store_date"]?.GetValue<string>(), o["cover_date"]?.GetValue<string>()));
                }
            }
        }

        return issues;
    }

    public async Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken)
    {
        JsonNode root;
        try
        {
            // 4000- is ComicVine's issue resource-type prefix.
            root = await GetAsync(Url($"issue/4000-{issueId}", $"field_list={IssueDetailFields}"), cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            return null;
        }

        return root["results"] is JsonObject results ? ParseIssueDetails(results) : null;
    }

    private static ComicVineIssueDetails ParseIssueDetails(JsonObject o)
    {
        var credits = new List<ComicVineCredit>();
        if (o["person_credits"] is JsonArray people)
        {
            foreach (var person in people.OfType<JsonObject>())
            {
                var name = person["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // ComicVine's person_credits carry a real id per person (confirmed present on the
                // live API shape, previously unread) - docs/superpowers/specs/2026-09-23-metron-api-
                // utilization-design.md. Unlike Metron, ComicVine's credit objects genuinely do
                // identify the creator, not just the role.
                int? personId = person["id"] is { } pid && int.TryParse(pid.ToString(), out int parsedPersonId) ? parsedPersonId : null;

                // Apply EVERY recognized role token, not just the first (CE's cvdb.py:686-691 loops
                // over every role a person has, not just one) - a person credited "writer, artist"
                // gets a Writer credit AND a Penciller AND an Inker credit, not just the first match.
                // Dedup in case two tokens resolve to the same field (e.g. "penciler, penciller").
                var resolvedFields = new List<string>();
                foreach (var token in (person["role"]?.GetValue<string>() ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (PersonRoleMap.TryGetValue(token, out var mapped))
                    {
                        foreach (var field in mapped)
                        {
                            if (!resolvedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                            {
                                resolvedFields.Add(field);
                            }
                        }
                    }
                }

                if (resolvedFields.Count == 0)
                {
                    credits.Add(new ComicVineCredit(name, null, CreatorExternalId: personId));
                }
                else
                {
                    foreach (var field in resolvedFields)
                    {
                        credits.Add(new ComicVineCredit(name, field, CreatorExternalId: personId));
                    }
                }
            }
        }

        var volume = o["volume"] as JsonObject;
        return new ComicVineIssueDetails(
            o["id"]?.GetValue<int>() ?? 0,
            volume?["id"]?.GetValue<int>() ?? 0,
            volume?["name"]?.GetValue<string>(),
            o["issue_number"]?.GetValue<string>(),
            o["name"]?.GetValue<string>(),
            o["site_detail_url"]?.GetValue<string>(),
            ParseDatePart(o["cover_date"]?.GetValue<string>()),
            ParseDatePart(o["store_date"]?.GetValue<string>()),
            StripHtml(o["description"]?.GetValue<string>()),
            IdNamesOf(o["story_arc_credits"]),
            IdNamesOf(o["character_credits"]),
            IdNamesOf(o["team_credits"]),
            IdNamesOf(o["location_credits"]),
            credits)
        {
            // The fork's Tags (Concepts): ComicVine's concept_credits, names only.
            Concepts = IdNamesOf(o["concept_credits"]).Select(c => c.Name).ToList(),
        };
            // Genre/AgeRating/Isbn/Upc/Universes are left at their record defaults for
            // ComicVine (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md) - none of
            // these fields is confirmed to exist on ComicVine's real API from anything in this repo,
            // and IssueDetailFields deliberately doesn't request unverified field names (a rejected or
            // silently-ignored field could otherwise degrade the whole request). Confirm against a
            // live response before requesting any of them.
    }

    private static IReadOnlyList<ComicVineIdName> IdNamesOf(JsonNode? array) =>
        array is JsonArray items
            ? items.OfType<JsonObject>()
                .Where(i => i["name"]?.GetValue<string>() is { Length: > 0 })
                .Select(i => new ComicVineIdName(
                    i["id"] is { } id && int.TryParse(id.ToString(), out int parsed) ? parsed : null,
                    i["name"]!.GetValue<string>()))
                .ToList()
            : new List<ComicVineIdName>();

    /// <summary>ComicVine dates arrive as <c>yyyy-MM-dd HH:mm:ss</c> (or just <c>yyyy-MM-dd</c>); any part can be missing, so this is not a single DateTime.</summary>
    private static ComicVineDatePart ParseDatePart(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return new ComicVineDatePart(null, null, null);
        }

        var segments = date.Split('-', StringSplitOptions.TrimEntries);
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

        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return System.Text.RegularExpressions.Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    public Paperbunkr.Data.Entities.ComicProvider Kind => Paperbunkr.Data.Entities.ComicProvider.ComicVine;

    private string Url(string path, string query) => $"{BaseUrl}/{path}/?api_key={Uri.EscapeDataString(_apiKey)}&format=json&{query}";

    /// <summary>CE's real <c>__get_dom</c> retries the whole request exactly once, after a flat 2.5s
    /// sleep, on ANY transport/parse-level failure - network error, empty body, XML/JSON parse failure
    /// (cvconnection.py:159-219, verified directly against source). Docs/superpowers/specs/2026-09-24-
    /// comicvine-scraper-fidelity-design.md §2.2. Deliberately NOT a retry trigger: a well-formed
    /// response with a non-1 <c>status_code</c> (rejected key, rate-limited, not-found) - that's an API-
    /// level answer, not a failure to get one, and is handled below, outside this retry.</summary>
    /// <summary>Mutable (not <c>readonly</c>) so tests can shrink it - a real 2.5s sleep per retried
    /// test would make the suite slow for no benefit, same test-seam shape as <see cref="MetronQuota.Clock"/>.</summary>
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(2500);

    private async Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken)
    {
        JsonNode? root = null;
        ComicVineException? lastFailure = null;

        for (int attempt = 0; attempt < 2 && root is null; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            string body;
            try
            {
                using var request = ComicVineHttp.Get(url, _priority);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                lastFailure = new ComicVineException($"ComicVine request failed: {ex.Message}", inner: ex);
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new ComicVineException("ComicVine did not respond in time.");
                continue;
            }

            try
            {
                root = JsonNode.Parse(body);
                if (root is null)
                {
                    lastFailure = new ComicVineException("ComicVine returned an empty response.");
                }
            }
            catch (JsonException ex)
            {
                lastFailure = new ComicVineException($"ComicVine returned an unexpected response: {ex.Message}", inner: ex);
            }
        }

        if (root is null)
        {
            throw lastFailure!;
        }

        int status = root["status_code"]?.GetValue<int>() ?? 0;
        if (status == 107)
        {
            // ComicVine's own "rate limit exceeded": pause every caller, not just this one.
            _rateLimiter?.ReportRateLimited();
        }

        if (root is null || status != 1)
        {
            string? error = root?["error"]?.GetValue<string>();
            throw new ComicVineException(status == 100 ? "ComicVine rejected the API key." : $"ComicVine API error {status}: {error}", status);
        }

        return root;
    }

    private static ComicVineVolume? ParseVolume(JsonNode? node)
    {
        if (node is not JsonObject o || o["id"] is null)
        {
            return null;
        }

        return new ComicVineVolume(
            o["id"]!.GetValue<int>(),
            o["name"]?.GetValue<string>() ?? string.Empty,
            (o["publisher"] as JsonObject)?["name"]?.GetValue<string>(),
            ParseYear(o["start_year"]),
            o["count_of_issues"]?.GetValue<int>() ?? 0,
            ImageUrl(o["image"]));
    }

    private static ComicVineIssue? ParseIssue(JsonNode? node)
    {
        if (node is not JsonObject o || o["id"] is null)
        {
            return null;
        }

        return new ComicVineIssue(
            o["id"]!.GetValue<int>(),
            o["issue_number"]?.GetValue<string>()?.Trim() ?? string.Empty,
            o["name"]?.GetValue<string>(),
            ParseDate(o["store_date"]),
            ParseDate(o["cover_date"]),
            ImageUrl(o["image"]),
            (o["volume"] as JsonObject)?["id"]?.GetValue<int>());
    }

    /// <summary>ComicVine sends <c>start_year</c> as a string (sometimes "2026", sometimes with junk like "1990?").</summary>
    private static int? ParseYear(JsonNode? node)
    {
        var text = node?.ToString();
        return text is { Length: >= 4 } && int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) ? year : null;
    }

    private static DateTime? ParseDate(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static string? ImageUrl(JsonNode? image) =>
        (image as JsonObject) is { } o ? (o["medium_url"] ?? o["small_url"])?.GetValue<string>() : null;
}
