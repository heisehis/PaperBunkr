using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClusterLibraryManager.ComicVine;

/// <summary>
/// ComicVine metadata scraper (design doc §3) - the 4 CE-verified endpoints, CE's exact throttle/retry
/// behavior, and JSON DTO mapping. Deliberate mechanical deviations from CE: `format=json` (not CE's
/// `format=xml`) and the User-Agent below (Paperbunkr's own plugin identity, not a CE clone) - same
/// endpoints, same fields otherwise.
/// </summary>
public sealed class ComicVineService
{
    private const string BaseUrl = "https://comicvine.gamespot.com/api";
    private const string UserAgent = "PaperBunkrComicVinePlugin/1.0";

    // CE's own throttle (cvconnection.py __QUERY_DELAY_MS=1100, verified): every request waits until
    // at least this long has passed since the previous one, host-wide (shared across every
    // ComicVineService instance, matching CE's own single-process throttle). Not `const` - tests
    // shrink this via the internal setter below rather than eating the real 1100ms per assertion.
    internal static int ThrottleMilliseconds { get; set; } = 1100;

    // CE's own retry (cvconnection.py __get_dom's `lasttry` pattern, verified): one flat retry after
    // this delay on failure/malformed response, then propagate. Not exponential backoff.
    internal static int RetryDelayMilliseconds { get; set; } = 2500;

    private static readonly HttpClient DefaultClient = CreateHttpClient();
    private static readonly SemaphoreSlim ThrottleGate = new(1, 1);
    private static DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    private readonly HttpClient _client;

    // CE's own person-role -> field mapping (cvdb.py person_credits handling, verified), onto
    // Paperbunkr's real Issue credit fields (Issue.cs, verified) rather than CE's ComicRack ones.
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

    private readonly string _apiKey;

    public ComicVineService(string apiKey) : this(apiKey, DefaultClient)
    {
    }

    /// <summary>Test seam - substitute an <see cref="HttpClient"/> wrapping a fake handler instead of
    /// the real shared static one.</summary>
    internal ComicVineService(string apiKey, HttpClient client)
    {
        _apiKey = apiKey;
        _client = client;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PaperBunkrComicVinePlugin", "1.0"));
        return client;
    }

    /// <summary>
    /// No dedicated CE test-connection endpoint exists (verified: CE only discovers a bad key lazily
    /// on first real scrape - see design doc §3's citation of `configform.py`'s OK-button-enable-only-
    /// if-nonempty logic). This issues the same volume-search call SearchVolumesAsync uses, with a
    /// fixed placeholder query, and reports whether the API accepted the key - the closest honest
    /// equivalent to a "test connection" action given CE itself has none.
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        string url = BuildUrl("search/", ("resources", "volume"), ("limit", "1"), ("query", "batman"));
        try
        {
            await GetAsync<List<ComicVineVolumeRaw>>(url, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ComicVineException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<(string, string)>
        {
            ("resources", "volume"),
            ("field_list", "name,start_year,publisher,id,image,count_of_issues"),
            ("query", query),
        };
        // CE comment (verified): omitting page=1 avoids a real ComicVine search bug.
        if (page > 1)
        {
            queryParams.Add(("page", page.ToString(CultureInfo.InvariantCulture)));
        }

        string url = BuildUrl("search/", queryParams.ToArray());
        ComicVineEnvelope<List<ComicVineVolumeRaw>>? envelope = await GetAsync<List<ComicVineVolumeRaw>>(url, cancellationToken).ConfigureAwait(false);
        return (envelope?.Results ?? new List<ComicVineVolumeRaw>()).Select(MapVolume).ToList();
    }

    public async Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default)
    {
        // 4050- is ComicVine's volume resource-type prefix (verified).
        string url = BuildUrl($"volume/4050-{volumeId}/", ("field_list", "name,start_year,publisher,image,count_of_issues,id"));
        ComicVineEnvelope<ComicVineVolumeRaw>? envelope = await GetAsync<ComicVineVolumeRaw>(url, cancellationToken).ConfigureAwait(false);
        return envelope?.Results is { } raw ? MapVolumeDetails(raw) : null;
    }

    public async Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<(string, string)>
        {
            ("field_list", "name,issue_number,id,image"),
            ("filter", $"volume:{volumeId}"),
        };
        if (page > 1)
        {
            queryParams.Add(("page", page.ToString(CultureInfo.InvariantCulture)));
        }

        string url = BuildUrl("issues/", queryParams.ToArray());
        ComicVineEnvelope<List<ComicVineIssueSummaryRaw>>? envelope = await GetAsync<List<ComicVineIssueSummaryRaw>>(url, cancellationToken).ConfigureAwait(false);
        return (envelope?.Results ?? new List<ComicVineIssueSummaryRaw>()).Select(MapIssueSummary).ToList();
    }

    public async Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default)
    {
        // 4000- is ComicVine's issue resource-type prefix (verified).
        string url = BuildUrl($"issue/4000-{issueId}/");
        ComicVineEnvelope<ComicVineIssueDetailsRaw>? envelope = await GetAsync<ComicVineIssueDetailsRaw>(url, cancellationToken).ConfigureAwait(false);
        return envelope?.Results is { } raw ? MapIssueDetails(raw) : null;
    }

    private string BuildUrl(string path, params (string Key, string Value)[] queryParams)
    {
        var builder = new StringBuilder($"{BaseUrl}/{path}?api_key={Uri.EscapeDataString(_apiKey)}&format=json");
        foreach ((string key, string value) in queryParams)
        {
            builder.Append('&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    private async Task<ComicVineEnvelope<T>?> GetAsync<T>(string url, CancellationToken cancellationToken) =>
        await GetAsyncCore<T>(url, lastTry: false, cancellationToken).ConfigureAwait(false);

    private async Task<ComicVineEnvelope<T>?> GetAsyncCore<T>(string url, bool lastTry, CancellationToken cancellationToken)
    {
        await WaitForThrottleAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using HttpResponseMessage response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ComicVineEnvelope<T>? envelope = JsonSerializer.Deserialize<ComicVineEnvelope<T>>(json);

            if (envelope is null || envelope.StatusCode != 1)
            {
                throw new ComicVineException(envelope?.StatusCode ?? -1, envelope?.Error ?? "Empty or malformed response from ComicVine.");
            }

            return envelope;
        }
        catch (Exception) when (!lastTry)
        {
            await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            return await GetAsyncCore<T>(url, lastTry: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForThrottleAsync(CancellationToken cancellationToken)
    {
        await ThrottleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan wait = _nextAllowedRequestUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _nextAllowedRequestUtc = DateTime.UtcNow.AddMilliseconds(ThrottleMilliseconds);
        }
        finally
        {
            ThrottleGate.Release();
        }
    }

    private static ComicVineVolumeSearchResult MapVolume(ComicVineVolumeRaw raw) =>
        new(raw.Id, raw.Name ?? string.Empty, raw.StartYear, raw.Publisher?.Name, raw.CountOfIssues, raw.Image?.BestUrl());

    private static ComicVineVolumeDetails MapVolumeDetails(ComicVineVolumeRaw raw) =>
        new(raw.Id, raw.Name ?? string.Empty, raw.StartYear, raw.Publisher?.Name, raw.CountOfIssues, raw.Image?.BestUrl());

    private static ComicVineIssueSummary MapIssueSummary(ComicVineIssueSummaryRaw raw) =>
        new(raw.Id, raw.IssueNumber, raw.Name, raw.Image?.BestUrl());

    private static ComicVineIssueDetails MapIssueDetails(ComicVineIssueDetailsRaw raw)
    {
        var credits = new List<ComicVineCredit>();
        foreach (ComicVinePersonCreditRaw person in raw.PersonCredits ?? new List<ComicVinePersonCreditRaw>())
        {
            if (person.Name is null)
            {
                continue;
            }

            string? field = null;
            foreach (string roleToken in (person.Role ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (PersonRoleMap.TryGetValue(roleToken, out string? mapped))
                {
                    field = mapped;
                    break;
                }
            }

            credits.Add(new ComicVineCredit(person.Name, field));
        }

        return new ComicVineIssueDetails(
            raw.Id,
            raw.Volume?.Id ?? 0,
            raw.Volume?.Name,
            raw.IssueNumber,
            raw.Name,
            raw.SiteDetailUrl,
            ParseDatePart(raw.CoverDate),
            ParseDatePart(raw.StoreDate),
            StripHtml(raw.Description),
            NamesOf(raw.StoryArcCredits),
            NamesOf(raw.CharacterCredits),
            NamesOf(raw.TeamCredits),
            NamesOf(raw.LocationCredits),
            credits);
    }

    private static IReadOnlyList<string> NamesOf(List<ComicVineNamedRefRaw>? refs) =>
        (refs ?? new List<ComicVineNamedRefRaw>())
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

    /// <summary>ComicVine dates arrive as `yyyy-MM-dd HH:mm:ss` (or just `yyyy-MM-dd`) - split into
    /// year/month/day parts (CE's own cover_date/store_date -> pub/release y-m-d split, verified),
    /// not a single .NET DateTime, since any part can be absent.</summary>
    private static ComicVineDatePart ParseDatePart(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return new ComicVineDatePart(null, null, null);
        }

        string[] segments = date.Split('-', StringSplitOptions.TrimEntries);
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

        string noTags = Regex.Replace(html, "<[^>]+>", " ");
        string decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
