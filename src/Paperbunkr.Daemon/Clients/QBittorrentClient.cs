using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Paperbunkr.Daemon.Indexers;

namespace Paperbunkr.Daemon.Clients;

/// <summary>
/// qBittorrent through its Web API v2 (cookie login, <c>Referer</c> set to the server's own address as the API requires). A .torrent URL is
/// fetched here and uploaded as a file, because that is the only way to know its info-hash before qBittorrent has parsed it; a magnet carries
/// its hash. Everything is category-locked - see <see cref="IDownloadClient"/>.
/// </summary>
public sealed class QBittorrentClient : IDownloadClient
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient SharedFetch = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };

    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;
    private readonly string _category;
    private readonly HttpClient _api;
    private readonly HttpClient _fetch;
    private bool _loggedIn;

    /// <param name="api">The client for qBittorrent's own API. Production passes none and gets a private cookie-keeping one; tests inject a fake.</param>
    /// <param name="fetch">The client used to download a .torrent from an indexer link (redirects are followed by hand so a redirect to a magnet is handled).</param>
    public QBittorrentClient(string baseUrl, string username, string password, string category, HttpClient? api = null, HttpClient? fetch = null)
    {
        _baseUrl = baseUrl.Trim().TrimEnd('/');
        _username = username;
        _password = password;
        _category = category.Trim();
        // A private handler per client instance: the login cookie belongs to one server + user, never shared statically.
        _api = api ?? new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() }) { Timeout = ApiTimeout };
        _fetch = fetch ?? SharedFetch;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = (await SendAsync(HttpMethod.Get, "/api/v2/app/version", null, cancellationToken).ConfigureAwait(false)).Trim();
            return ConnectionTestResult.Ok($"Connected to qBittorrent {version}. Downloads go in the \"{_category}\" category.");
        }
        catch (DownloadClientException ex)
        {
            return ConnectionTestResult.Fail(ex.Message);
        }
    }

    public async Task<string> AddAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_category))
        {
            throw new DownloadClientException("Set a qBittorrent category first: Paperbunkr only ever touches torrents in its own category.");
        }

        var (magnet, torrentBytes) = await ResolveAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        string? hash = magnet is not null ? TorrentHash.FromMagnet(magnet) : TorrentHash.FromTorrentFile(torrentBytes!);
        if (hash is null)
        {
            throw new DownloadClientException("That release isn't a valid magnet link or .torrent file.");
        }

        HttpContent Build()
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(_category), "category" },
            };
            if (magnet is not null)
            {
                form.Add(new StringContent(magnet), "urls");
            }
            else
            {
                var file = new ByteArrayContent(torrentBytes!);
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
                form.Add(file, "torrents", "release.torrent");
            }

            return form;
        }

        var body = await SendAsync(HttpMethod.Post, "/api/v2/torrents/add", Build, cancellationToken).ConfigureAwait(false);

        // "Fails." is what qBittorrent answers for an invalid torrent *and* for one it already has. Already having it, in our category, is fine.
        if (body.Contains("Fails", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await GetStatusAsync(new[] { hash }, cancellationToken).ConfigureAwait(false);
            if (existing.Count == 0)
            {
                throw new DownloadClientException("qBittorrent refused the torrent (it may be invalid, or already added outside the Paperbunkr category).");
            }
        }

        return hash;
    }

    public async Task<IReadOnlyList<DownloadStatus>> GetStatusAsync(IReadOnlyCollection<string>? hashes, CancellationToken cancellationToken)
    {
        var query = $"category={Uri.EscapeDataString(_category)}";
        if (hashes is { Count: > 0 })
        {
            query += "&hashes=" + Uri.EscapeDataString(string.Join('|', hashes.Select(h => h.ToLowerInvariant())));
        }

        var json = await SendAsync(HttpMethod.Get, $"/api/v2/torrents/info?{query}", null, cancellationToken).ConfigureAwait(false);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DownloadClientException("qBittorrent returned something that isn't a torrent list.", ex);
        }

        if (root is not JsonArray list)
        {
            throw new DownloadClientException("qBittorrent returned something that isn't a torrent list.");
        }

        return list
            .OfType<JsonObject>()
            // Belt and braces: even if a server ignored the category filter, never surface a torrent from another one.
            .Where(o => string.Equals(o["category"]?.GetValue<string>(), _category, StringComparison.Ordinal))
            .Select(ToStatus)
            .ToList();
    }

    public async Task<IReadOnlyList<DownloadFile>> GetFilesAsync(string hash, CancellationToken cancellationToken)
    {
        if ((await GetStatusAsync(new[] { hash }, cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            return Array.Empty<DownloadFile>();   // not ours: never list another category's files
        }

        var json = await SendAsync(HttpMethod.Get, $"/api/v2/torrents/files?hash={Uri.EscapeDataString(hash.ToLowerInvariant())}", null, cancellationToken).ConfigureAwait(false);
        var list = JsonNode.Parse(json) as JsonArray ?? throw new DownloadClientException("qBittorrent returned something that isn't a file list.");
        return list.OfType<JsonObject>()
            .Select(o => new DownloadFile(o["name"]?.GetValue<string>() ?? string.Empty, o["size"]?.GetValue<long>() ?? 0))
            .Where(f => f.Path.Length > 0)
            .ToList();
    }

    public async Task<bool> RemoveAsync(string hash, bool deleteFiles, CancellationToken cancellationToken)
    {
        if ((await GetStatusAsync(new[] { hash }, cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            return false;
        }

        HttpContent Build() => new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["hashes"] = hash.ToLowerInvariant(),
            ["deleteFiles"] = deleteFiles ? "true" : "false",
        });

        await SendAsync(HttpMethod.Post, "/api/v2/torrents/delete", Build, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static DownloadStatus ToStatus(JsonObject o)
    {
        double progress = o["progress"]?.GetValue<double>() ?? 0;
        string state = o["state"]?.GetValue<string>() ?? string.Empty;
        long eta = o["eta"]?.GetValue<long>() ?? 8640000;

        return new DownloadStatus(
            (o["hash"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant(),
            o["name"]?.GetValue<string>() ?? string.Empty,
            progress,
            MapState(state, progress),
            o["size"]?.GetValue<long>() ?? 0,
            o["dlspeed"]?.GetValue<long>() ?? 0,
            eta is > 0 and < 8640000 ? TimeSpan.FromSeconds(eta) : null,
            o["save_path"]?.GetValue<string>(),
            o["content_path"]?.GetValue<string>());
    }

    /// <summary>Maps qBittorrent's state strings (v4 <c>pausedDL</c> and v5 <c>stoppedDL</c> alike) onto <see cref="DownloadState"/>.</summary>
    internal static DownloadState MapState(string state, double progress)
    {
        if (state is "error")
        {
            return DownloadState.Error;
        }

        if (state is "missingFiles")
        {
            return DownloadState.Missing;
        }

        if (progress >= 0.9999)
        {
            return DownloadState.Completed;   // seeding, stalledUP, pausedUP/stoppedUP, queuedUP, forcedUP, checkingUP... all mean "the data is all there"
        }

        if (state.StartsWith("paused", StringComparison.Ordinal) || state.StartsWith("stopped", StringComparison.Ordinal))
        {
            return DownloadState.Paused;
        }

        if (state.StartsWith("queued", StringComparison.Ordinal))
        {
            return DownloadState.Queued;
        }

        return DownloadState.Downloading;
    }

    /// <summary>A magnet link is used as-is; an http(s) link is downloaded (following redirects by hand, since one may lead to a magnet).</summary>
    private async Task<(string? Magnet, byte[]? Torrent)> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return (url, null);
        }

        var current = url;
        for (int hop = 0; hop < 6; hop++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _fetch.GetAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadClientException($"Couldn't download the release from the indexer: {ex.Message}", ex);
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(current), location).ToString();
                    if (next.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    {
                        return (next, null);
                    }

                    current = next;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DownloadClientException($"The indexer answered HTTP {(int)response.StatusCode} for that release.");
                }

                return (null, await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        throw new DownloadClientException("Too many redirects fetching the release.");
    }

    /// <summary>One authenticated call. Logs in on first use and once more if the session has expired (HTTP 403).</summary>
    private async Task<string> SendAsync(HttpMethod method, string path, Func<HttpContent>? content, CancellationToken cancellationToken)
    {
        if (!_loggedIn)
        {
            await LoginAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.Referrer = new Uri(_baseUrl);
            if (content is not null)
            {
                request.Content = content();
            }

            HttpResponseMessage response;
            try
            {
                response = await _api.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadClientException($"Couldn't reach qBittorrent at {_baseUrl}: {ex.Message}", ex);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DownloadClientException("qBittorrent did not respond in time.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Forbidden && attempt == 0)
                {
                    _loggedIn = false;
                    await LoginAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DownloadClientException($"qBittorrent answered HTTP {(int)response.StatusCode}.");
                }

                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        throw new DownloadClientException("qBittorrent refused the request even after logging in again.");
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/v2/auth/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = _username, ["password"] = _password }),
        };
        request.Headers.Referrer = new Uri(_baseUrl);

        try
        {
            using var response = await _api.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new DownloadClientException("qBittorrent has banned this address after too many failed logins. Wait a while, or clear the ban in qBittorrent.");
            }

            if (!response.IsSuccessStatusCode || body.Contains("Fails", StringComparison.OrdinalIgnoreCase))
            {
                throw new DownloadClientException("qBittorrent rejected the username or password.");
            }

            _loggedIn = true;
        }
        catch (HttpRequestException ex)
        {
            throw new DownloadClientException($"Couldn't reach qBittorrent at {_baseUrl}: {ex.Message}", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadClientException("qBittorrent did not respond in time.");
        }
    }
}
