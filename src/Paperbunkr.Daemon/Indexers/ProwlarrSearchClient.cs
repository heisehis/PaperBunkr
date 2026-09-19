using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.Daemon.Indexers;

/// <summary>
/// Searches through a user-supplied Prowlarr instance using Prowlarr's native <c>GET /api/v1/search</c>.
/// Prowlarr has no combined Torznab feed - its per-indexer Torznab endpoints (<c>/{id}/api</c>) each hit a
/// single tracker - so the native JSON search is the way to query every configured indexer in one call
/// (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §2). Response field names follow
/// Prowlarr's <c>ReleaseResource</c>. Only torrent results are returned (qBittorrent is the download client).
/// </summary>
public sealed class ProwlarrSearchClient : IIndexerClient
{
    /// <summary>Newznab/Torznab category "Books/Comics".</summary>
    public const int ComicsCategory = 7030;

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly IReadOnlyList<int> _categories;

    public ProwlarrSearchClient(string baseUrl, string apiKey, HttpClient? http = null, IReadOnlyList<int>? categories = null)
    {
        _baseUrl = baseUrl.Trim().TrimEnd('/');
        _apiKey = apiKey;
        _http = http ?? SharedHttp;
        _categories = categories ?? new[] { ComicsCategory };
    }

    public async Task<IReadOnlyList<IndexerRelease>> SearchAsync(string queryText, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/api/v1/search?query={Uri.EscapeDataString(queryText)}&type=search&limit=100"
            + string.Concat(_categories.Select(c => $"&categories={c}"));

        var body = await GetAsync(url, cancellationToken).ConfigureAwait(false);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new IndexerException($"Prowlarr returned an unexpected response: {ex.Message}", ex);
        }

        if (root is not JsonArray results)
        {
            throw new IndexerException("Prowlarr returned an unexpected response (expected a list of results).");
        }

        return results.Select(Parse).OfType<IndexerRelease>().ToList();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var body = await GetAsync($"{_baseUrl}/api/v1/system/status", cancellationToken).ConfigureAwait(false);
            var version = JsonNode.Parse(body)?["version"]?.GetValue<string>();
            return ConnectionTestResult.Ok(version is null ? "Connected to Prowlarr." : $"Connected to Prowlarr {version}.");
        }
        catch (IndexerException ex)
        {
            return ConnectionTestResult.Fail(ex.Message);
        }
        catch (JsonException)
        {
            return ConnectionTestResult.Fail("That address answered, but it doesn't look like Prowlarr.");
        }
    }

    private async Task<string> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", _apiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new IndexerException("Prowlarr rejected the API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IndexerException($"Prowlarr answered HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new IndexerException($"Couldn't reach Prowlarr at {_baseUrl}: {ex.Message}", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IndexerException("Prowlarr did not respond in time.");
        }
    }

    private static IndexerRelease? Parse(JsonNode? node)
    {
        if (node is not JsonObject o || !IsTorrent(o["protocol"]))
        {
            return null;
        }

        var title = o["title"]?.GetValue<string>();
        var magnet = o["magnetUrl"]?.GetValue<string>();
        var download = o["downloadUrl"]?.GetValue<string>();
        var link = !string.IsNullOrWhiteSpace(magnet) ? magnet : download;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        return new IndexerRelease
        {
            Title = title,
            DownloadUrl = link,
            SizeBytes = o["size"]?.GetValue<long>() ?? 0,
            Seeders = o["seeders"]?.GetValue<int>() ?? 0,
            Peers = o["leechers"]?.GetValue<int>() ?? 0,
            Indexer = o["indexer"]?.GetValue<string>(),
            Guid = o["guid"]?.GetValue<string>(),
            PublishedAt = DateTimeOffset.TryParse(o["publishDate"]?.GetValue<string>(), out var published) ? published : null,
        };
    }

    /// <summary>Prowlarr serializes <c>DownloadProtocol</c> as "torrent"/"usenet" (older builds: 2/1).</summary>
    private static bool IsTorrent(JsonNode? protocol)
    {
        if (protocol is null)
        {
            return false;
        }

        var text = protocol.ToString();
        return text.Equals("torrent", StringComparison.OrdinalIgnoreCase) || text == "2";
    }
}
