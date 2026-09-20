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
public sealed class MetronClient : IComicProvider
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
                if (roleName is not null && RoleMap.TryGetValue(roleName.Trim(), out var mapped))
                {
                    // One credit can carry several roles (writer and artist); each is its own credit line so every field is filled.
                    credits.Add(new ComicVineCredit(name, mapped));
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
            Names(o["arcs"]),
            Names(o["characters"]),
            Names(o["teams"]),
            Array.Empty<string>(),      // Metron has no locations
            credits);
    }

    private static IReadOnlyList<string> Names(JsonNode? array) =>
        array is JsonArray items
            ? items.OfType<JsonObject>().Select(i => i["name"]?.GetValue<string>()).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList()
            : new List<string>();

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

    private async Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken)
    {
        string body;
        HttpStatusCode status;
        try
        {
            using var request = ComicVineHttp.Get(url, _priority);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authorization);
            request.Headers.UserAgent.ParseAdd("Paperbunkr (comic library manager)");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ComicVineException($"Metron request failed: {ex.Message}", inner: ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComicVineException("Metron did not respond in time.");
        }

        // The status codes reuse ComicVine's numbering so callers treat both providers alike: 100 = the credentials were refused, 101 = not found, 107 = rate limited.
        switch (status)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new ComicVineException("Metron rejected your login. Check it under Preferences → Connections.", 100);
            case HttpStatusCode.NotFound:
                throw new ComicVineException("Metron has no such record.", 101);
            case HttpStatusCode.TooManyRequests:
                throw new ComicVineException("Metron's rate limit was reached; try again in a minute.", 107);
        }

        if ((int)status >= 400)
        {
            throw new ComicVineException($"Metron returned HTTP {(int)status}.");
        }

        try
        {
            return JsonNode.Parse(body) ?? throw new ComicVineException("Metron returned an empty response.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ComicVineException($"Metron returned an unexpected response: {ex.Message}", inner: ex);
        }
    }
}
