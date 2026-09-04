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

    // Metron doesn't publish a documented rate limit the way ComicVine does; a shorter minimum
    // spacing is a reasonable default courtesy throttle.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _http;
    private readonly object _throttleLock = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public MetronSource(string username, string password)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/0.1 (comic library manager)");
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

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

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        var result = new List<ArcIssue>();

        // /issue_list/ is a standard paginated DRF list response ({count,next,previous,results}) -
        // the queryset is already ordered by cover_date/series/number server-side, so pages are
        // appended in order and never re-sorted here.
        string? url = $"{BaseUrl}/arc/{Uri.EscapeDataString(arcId)}/issue_list/";
        while (!string.IsNullOrEmpty(url))
        {
            var page = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in page?["results"]?.AsArray() ?? new JsonArray())
            {
                if (node is null)
                {
                    continue;
                }

                string? coverDate = node["cover_date"]?.GetValue<string>();
                string? storeDate = node["store_date"]?.GetValue<string>();
                int year = ParseYearFromDate(coverDate) ?? ParseYearFromDate(storeDate) ?? 0;

                result.Add(new ArcIssue(
                    Series: node["series"]?["name"]?.GetValue<string>() ?? string.Empty,
                    Number: node["number"]?.GetValue<string>() ?? string.Empty,
                    Year: year,
                    CoverImageUrl: node["image"]?.GetValue<string>()));
            }

            url = page?["next"]?.GetValue<string>();
        }

        return result;
    }

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        var detail = await GetJsonAsync($"{BaseUrl}/arc/{Uri.EscapeDataString(arcId)}/", cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        return new ArcOverviewInfo(detail["desc"]?.GetValue<string>(), detail["image"]?.GetValue<string>());
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
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReadingListSourceException(DisplayName, $"Metron request failed: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsing throws TaskCanceledException, not HttpRequestException.
            throw new ReadingListSourceException(DisplayName, "Metron did not respond within 20 seconds.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ReadingListSourceException(DisplayName, "Metron rejected the configured username/password (401 Unauthorized).");
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
}
