using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// Thin wrapper over the Metron API (metron.cloud) - ported from the real, bug-fixed
/// <c>_reference/CBLManager/src/CBLManager/MetronSource.cs</c>. That source's own history matters
/// here: an earlier version read arc issues from <c>/api/arc/{id}/</c>'s own "issues" field, which
/// doesn't exist (confirmed from Metron's real open-source serializers, not a guess) - the real
/// issue list lives at the separate, paginated <c>/api/arc/{id}/issue_list/</c>. Reusing that fix
/// rather than re-deriving the endpoint shape from scratch.
/// </summary>
public sealed class MetronSource : IReadingListSource
{
    private const string BaseUrl = "https://metron.cloud/api";

    // Every request goes through the app's shared Metron client, so its rate limiter (18 a minute, foreground first) counts these alongside the
    // scraper's and the account sync's. This source used to keep its own client spaced only 0.5 s apart, which Metron's 20-a-minute limit
    // answered with "Request was throttled" (reported 2026-10-29, creating a reading list from Metron).
    private readonly HttpClient _http;
    private readonly ComicVine.MetronAuth _auth;

    public MetronSource(string username, string password)
        : this(ComicVine.MetronAuth.Basic(username, password))
    {
    }

    public MetronSource(ComicVine.MetronAuth auth, HttpClient? http = null)
    {
        _http = http ?? ComicVine.MetronHttp.Client;
        _auth = auth;
    }

    /// <summary>The longest "try again in N seconds" this source waits out before giving up; Metron's per-minute throttle never asks for more.</summary>
    internal static TimeSpan MaxThrottleWait { get; set; } = TimeSpan.FromSeconds(65);

    /// <summary>Test seam for the wait itself.</summary>
    internal static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, ct) => Task.Delay(t, ct);

    public string SourceKey => "Metron";
    public string DisplayName => "Metron";
    public bool RequiresCredentials => true;
    public bool HasBrowsableCatalog => false;

    public async Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string url = $"{BaseUrl}/arc/?name={Uri.EscapeDataString(query)}";
        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var results = root?["results"]?.AsArray() ?? new JsonArray();

        var list = new List<ArcSearchResult>();
        foreach (var node in results)
        {
            if (node is null)
            {
                continue;
            }

            // ArcListSerializer only returns (id, name, modified) - Deck/IssueCount are always
            // empty/0 for this source, not a bug, just nothing there to show (confirmed from
            // Metron's real serializer source).
            list.Add(new ArcSearchResult(
                Id: node["id"]!.ToString(),
                Name: node["name"]?.GetValue<string>() ?? string.Empty,
                Deck: null,
                Publisher: null,
                IssueCount: 0));
        }

        return list;
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken) =>
        (await GetArcEntriesAsync(arcId, cancellationToken).ConfigureAwait(false)).Issues;

    /// <summary>The paginated list also says how many issues the arc has (<c>count</c>), so a short fetch shows as incomplete.</summary>
    public async Task<ArcEntryList> GetArcEntriesAsync(string arcId, CancellationToken cancellationToken)
    {
        var result = new List<ArcIssue>();
        int? reported = null;

        // /issue_list/ is a standard paginated DRF list response ({count,next,previous,results}) -
        // the queryset is already ordered by cover_date/series/number server-side, so pages are
        // appended in order and never re-sorted here.
        string? url = $"{BaseUrl}/arc/{Uri.EscapeDataString(arcId)}/issue_list/";
        while (!string.IsNullOrEmpty(url))
        {
            var page = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            reported ??= page?["count"] is JsonValue count && int.TryParse(count.ToString(), out int total) ? total : null;
            foreach (var node in page?["results"]?.AsArray() ?? new JsonArray())
            {
                if (node is null)
                {
                    continue;
                }

                result.Add(ParseArcIssue(node));
            }

            url = page?["next"]?.GetValue<string>();
        }

        return new ArcEntryList(result, Array.Empty<string>(), reported);
    }

    /// <summary>One row of Metron's arc issue list as an <see cref="ArcIssue"/>. A story title is read if the row carries one (a <c>name</c>
    /// that is a string or a list of strings) - <b>not verified against a live response</b> (Metron's docs could not be fetched); when the row
    /// has none, the issue simply has no title and role detection falls back to what the library itself knows.</summary>
    public static ArcIssue ParseArcIssue(JsonNode node)
    {
        string? coverDate = node["cover_date"]?.GetValue<string>();
        string? storeDate = node["store_date"]?.GetValue<string>();
        string? title = node["name"] switch
        {
            JsonArray names => string.Join("; ", names.Select(n => n?.GetValue<string>()).Where(n => !string.IsNullOrWhiteSpace(n))),
            JsonValue value when value.TryGetValue(out string? single) => single,
            _ => null,
        };

        return new ArcIssue(
            Series: node["series"]?["name"]?.GetValue<string>() ?? string.Empty,
            Number: node["number"]?.GetValue<string>() ?? string.Empty,
            Year: ParseYearFromDate(coverDate) ?? ParseYearFromDate(storeDate) ?? 0,
            CoverImageUrl: node["image"]?.GetValue<string>(),
            Title: string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            // Read when the row carries them and left empty when it does not: the list rows' exact fields were not checked against a live
            // response, and an id is never made up (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.3).
            ComicVineIssueId: PositiveId(node["cv_id"]),
            MetronIssueId: PositiveId(node["id"]),
            MetronSeriesId: PositiveId(node["series"]?["id"]));
    }

    private static string? PositiveId(JsonNode? node) =>
        node is JsonValue value && long.TryParse(value.ToString(), out long id) && id > 0 ? id.ToString() : null;

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        var detail = await GetJsonAsync($"{BaseUrl}/arc/{Uri.EscapeDataString(arcId)}/", cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        // cv_id: Metron's ArcSerializer fields are (id, name, desc, image, cv_id, gcd_id, resource_url, modified) - checked against
        // Metron-Project/metron api/v1_0/serializers/arc.py for the Story Event resolver (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §3).
        string? cvId = detail["cv_id"] is JsonValue cv && long.TryParse(cv.ToString(), out long parsedCv) && parsedCv > 0 ? parsedCv.ToString() : null;
        return new ArcOverviewInfo(detail["desc"]?.GetValue<string>(), detail["image"]?.GetValue<string>(), cvId);
    }

    private static int? ParseYearFromDate(string? date)
    {
        if (!string.IsNullOrEmpty(date) && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out int year))
        {
            return year;
        }
        return null;
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        // Shared with every other Metron request the app makes: a rejected login must not be sent a third time (see MetronLoginGuard).
        if (ComicVine.MetronLoginGuard.ShouldHold(_auth.Key, out var hold))
        {
            throw new ReadingListSourceException(DisplayName, ComicVine.MetronLoginGuard.HoldMessage(hold));
        }

        HttpResponseMessage response;
        string body;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Options.Set(ComicVine.ComicVineRateLimitHandler.PriorityKey, ComicVine.ComicVineRequestPriority.High);
                request.Headers.Authorization = _auth.Header();
                request.Headers.UserAgent.ParseAdd("Paperbunkr (comic library manager)");
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                ComicVine.MetronQuota.Observe(response.Headers);
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new ReadingListSourceException(DisplayName, $"Metron request failed: {ex.Message}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The shared client's own per-request timeout (20 s of sending, not queueing) elapsed.
                throw new ReadingListSourceException(DisplayName, "Metron did not respond within 20 seconds.");
            }

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 2)
            {
                break;
            }

            // "Request was throttled. Expected available in 19 seconds." is a wait, not a failure: wait it out (the shared limiter has paused
            // every other Metron caller for the same time) and ask again.
            var wait = ThrottleWait(response, body);
            response.Dispose();
            if (wait > MaxThrottleWait)
            {
                throw new ReadingListSourceException(DisplayName, "Metron's daily request limit has been reached; try again later.");
            }

            await Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        using var _ = response;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            ComicVine.MetronLoginGuard.RecordRejected(_auth.Key);
            throw new ReadingListSourceException(DisplayName, _auth.IsToken ? "Metron rejected the configured API token (401 Unauthorized)." : "Metron rejected the configured username/password (401 Unauthorized).");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ReadingListSourceException(DisplayName, $"Metron returned an unexpected response: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            string? detailMessage = root?["detail"]?.GetValue<string>();
            throw new ReadingListSourceException(DisplayName, $"Metron API error ({(int)response.StatusCode}): {detailMessage ?? response.ReasonPhrase}");
        }

        return root;
    }

    /// <summary>How long Metron asked to wait: its Retry-After header, else the seconds in "Expected available in N seconds", else 20 s.</summary>
    internal static TimeSpan ThrottleWait(HttpResponseMessage response, string body)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta + TimeSpan.FromSeconds(1);
        }

        var match = System.Text.RegularExpressions.Regex.Match(body ?? string.Empty, @"available in (\d+) second");
        return match.Success && int.TryParse(match.Groups[1].Value, out int seconds)
            ? TimeSpan.FromSeconds(seconds + 1)
            : TimeSpan.FromSeconds(20);
    }
}
