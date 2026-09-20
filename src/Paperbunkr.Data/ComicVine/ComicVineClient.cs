using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.Data.ComicVine;

public sealed record ComicVineVolume(int Id, string Name, string? Publisher, int? StartYear, int CountOfIssues, string? ImageUrl);

public sealed record ComicVineIssue(int Id, string IssueNumber, string? Name, DateTime? StoreDate, DateTime? CoverDate, string? ImageUrl, int? VolumeId);

/// <summary>ComicVine could not be reached, rejected the request, or returned something unparseable.</summary>
public sealed class ComicVineException(string message, int? apiStatusCode = null, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>ComicVine's own <c>status_code</c> when it answered (100 = invalid key, 101 = not found, 107 = rate limit...).</summary>
    public int? ApiStatusCode { get; } = apiStatusCode;
}

/// <summary>Volume and issue lookups for the acquisition daemon and the "track this series" UI.</summary>
public interface IComicVineClient
{
    Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken);

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
public sealed class ComicVineClient : IComicVineClient, IComicVineIssueDetailsSource
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

    /// <summary>ComicVine's own person-role -> Paperbunkr credit field mapping (CE's <c>cvdb.py</c> person_credits handling, as verified in the plugin this was ported from).</summary>
    private static readonly IReadOnlyDictionary<string, string> PersonRoleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["writer"] = "Writer",
        ["penciler"] = "Penciller",
        ["penciller"] = "Penciller",
        ["artist"] = "Penciller",
        ["inker"] = "Inker",
        ["cover"] = "CoverArtist",
        ["editor"] = "Editor",
        ["colorer"] = "Colorist",
        ["colorist"] = "Colorist",
        ["letterer"] = "Letterer",
    };

    private const string IssueDetailFields = "id,name,issue_number,site_detail_url,cover_date,store_date,description,volume,story_arc_credits,character_credits,team_credits,location_credits,person_credits";

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

                string? field = null;
                foreach (var token in (person["role"]?.GetValue<string>() ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (PersonRoleMap.TryGetValue(token, out var mapped))
                    {
                        field = mapped;
                        break;
                    }
                }

                credits.Add(new ComicVineCredit(name, field));
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
            NamesOf(o["story_arc_credits"]),
            NamesOf(o["character_credits"]),
            NamesOf(o["team_credits"]),
            NamesOf(o["location_credits"]),
            credits);
    }

    private static IReadOnlyList<string> NamesOf(JsonNode? array) =>
        array is JsonArray items
            ? items.OfType<JsonObject>().Select(i => i["name"]?.GetValue<string>()).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList()
            : new List<string>();

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

    private string Url(string path, string query) => $"{BaseUrl}/{path}/?api_key={Uri.EscapeDataString(_apiKey)}&format=json&{query}";

    private async Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken)
    {
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
            throw new ComicVineException($"ComicVine request failed: {ex.Message}", inner: ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComicVineException("ComicVine did not respond in time.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ComicVineException($"ComicVine returned an unexpected response: {ex.Message}", inner: ex);
        }

        int status = root?["status_code"]?.GetValue<int>() ?? 0;
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
