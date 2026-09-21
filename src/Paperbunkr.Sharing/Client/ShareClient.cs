using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Paperbunkr.Sharing.Protocol;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Sharing.Client;

/// <summary>Base for every failure the client surfaces to the UI; messages are user-safe.</summary>
public abstract class ShareClientException : Exception
{
    protected ShareClientException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The host is unreachable (down, wrong address, firewall, timeout).</summary>
public sealed class HostUnreachableException : ShareClientException
{
    public HostUnreachableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The host presented a different certificate than the one the user trusted. Never retried; needs an explicit re-trust.</summary>
public sealed class CertificateMismatchException : ShareClientException
{
    public CertificateMismatchException(string expected, string actual)
        : base("The host's certificate changed since you trusted it.")
    {
        Expected = expected;
        Actual = actual;
    }

    public string Expected { get; }

    public string Actual { get; }
}

public sealed class AuthFailedException : ShareClientException
{
    public AuthFailedException() : base("The password was rejected.") { }
}

public sealed class LockedOutException : ShareClientException
{
    public LockedOutException(TimeSpan retryAfter) : base("Too many failed attempts. Try again later.") => RetryAfter = retryAfter;

    public TimeSpan RetryAfter { get; }
}

public sealed class ProtocolMismatchException : ShareClientException
{
    public ProtocolMismatchException(int host, int client)
        : base($"The host speaks protocol {host}, this app speaks {client}. Update Paperbunkr on the older side.")
    {
        HostVersion = host;
        ClientVersion = client;
    }

    public int HostVersion { get; }

    public int ClientVersion { get; }
}

/// <summary>Outcome of a first contact: who the host is, and the fingerprint the user is being asked to trust.</summary>
public sealed record ProbeResult(HelloResponse Hello, string Fingerprint);

/// <summary>One catalog fetch: either "unchanged since your ETag" or the full catalog.</summary>
public sealed record CatalogPull(bool NotModified, string? ETag, IReadOnlyList<CatalogSeriesDto> Series, IReadOnlyList<CatalogIssueDto> Issues);

/// <summary>
/// The client for a remote share (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §5/§6).
/// It never trusts a certificate chain: the connection succeeds only if the host's certificate matches
/// the pinned SHA-256 fingerprint the user accepted (trust-on-first-use), so a mismatch cannot be
/// silently overridden - it surfaces as <see cref="CertificateMismatchException"/> and stops.
/// </summary>
public sealed class ShareClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly PinnedHandler _handler;
    private readonly string _password;
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private string? _token;

    /// <param name="pinnedFingerprint">Uppercase hex SHA-256 the host's certificate must match.</param>
    public ShareClient(string host, int port, string pinnedFingerprint, string password, TimeSpan? timeout = null)
    {
        if (!IsValidFingerprint(pinnedFingerprint))
        {
            throw new ArgumentException("The pinned fingerprint must be a 64-character SHA-256 hex string.", nameof(pinnedFingerprint));
        }

        _handler = new PinnedHandler(pinnedFingerprint);
        _http = new HttpClient(_handler, disposeHandler: true)
        {
            BaseAddress = new UriBuilder(Uri.UriSchemeHttps, host, port).Uri,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
        _password = password;
    }

    /// <summary>
    /// First contact for the "Add remote library" flow: connects accepting <em>any</em> certificate, but
    /// only to read <c>/v1/hello</c> and report the fingerprint for the user to trust. Nothing
    /// authenticated (and no password) is ever sent over this unpinned connection.
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        string? fingerprint = null;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                fingerprint = cert is null ? null : Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
                return true;
            },
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new UriBuilder(Uri.UriSchemeHttps, host, port).Uri,
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };

        try
        {
            using HttpResponseMessage response = await http.GetAsync("/v1/hello", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            CheckProtocol(response);
            var hello = await response.Content.ReadFromJsonAsync<HelloResponse>(Json, cancellationToken).ConfigureAwait(false)
                        ?? throw new HostUnreachableException("The host sent an empty response.");
            return new ProbeResult(hello, fingerprint ?? throw new HostUnreachableException("The host did not present a certificate."));
        }
        catch (HttpRequestException ex)
        {
            throw new HostUnreachableException($"Could not reach {host}:{port}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HostUnreachableException($"Timed out reaching {host}:{port}.", ex);
        }
    }

    private static bool IsValidFingerprint(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    /// <summary>Re-reads <c>/v1/hello</c> over the pinned connection (used to detect a changed instance id).</summary>
    public async Task<HelloResponse> HelloAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "/v1/hello"), authenticated: false, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<HelloResponse>(Json, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>Pulls the whole catalog (all pages). With <paramref name="ifNoneMatch"/> an unchanged host answers 304 and <see cref="CatalogPull.NotModified"/> is true.</summary>
    public async Task<CatalogPull> GetCatalogAsync(string? ifNoneMatch, int pageSize = 200, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var series = new Dictionary<int, CatalogSeriesDto>();
        var issues = new List<CatalogIssueDto>();
        string? etag = null;
        string? cursor = null;
        bool first = true;

        do
        {
            string url = $"/v1/catalog?pageSize={pageSize}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            string? match = first ? ifNoneMatch : null;
            using HttpResponseMessage response = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (match is not null)
                {
                    request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(match));
                }

                return request;
            }, authenticated: true, cancellationToken, allowNotModified: first).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new CatalogPull(true, ifNoneMatch, Array.Empty<CatalogSeriesDto>(), Array.Empty<CatalogIssueDto>());
            }

            CatalogPage page = (await response.Content.ReadFromJsonAsync<CatalogPage>(Json, cancellationToken).ConfigureAwait(false))!;
            etag ??= response.Headers.ETag?.Tag;
            foreach (CatalogSeriesDto s in page.Series)
            {
                series[s.Id] = s;
            }

            issues.AddRange(page.Issues);
            progress?.Report(issues.Count);
            cursor = page.NextCursor;
            first = false;
        }
        while (cursor is not null);

        return new CatalogPull(false, etag, series.Values.ToList(), issues);
    }

    public async Task<PagesResponse> GetPagesAsync(int remoteIssueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"/v1/issues/{remoteIssueId}/pages"), true, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PagesResponse>(Json, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>Original page bytes, or a JPEG downscaled to <paramref name="maxWidth"/> when given. Null when the host says the page is gone (404).</summary>
    public async Task<byte[]?> GetPageBytesAsync(int remoteIssueId, int pageIndex, int? maxWidth = null, CancellationToken cancellationToken = default)
    {
        string url = $"/v1/issues/{remoteIssueId}/pages/{pageIndex}" + (maxWidth is null ? "" : $"?w={maxWidth}");
        return await GetBytesOrNullAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetCoverBytesAsync(int remoteIssueId, int? maxWidth = null, CancellationToken cancellationToken = default)
    {
        string url = $"/v1/issues/{remoteIssueId}/cover" + (maxWidth is null ? "" : $"?w={maxWidth}");
        return await GetBytesOrNullAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _http.Dispose();
        _loginGate.Dispose();
    }

    private async Task<byte[]?> GetBytesOrNullAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), true, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request, logging in first when needed and once more if the host answers 401 (its
    /// sessions live in memory, so a restarted host invalidates ours). Maps every transport and HTTP
    /// failure to a typed <see cref="ShareClientException"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> build,
        bool authenticated,
        CancellationToken cancellationToken,
        bool allowNotModified = false,
        bool allowNotFound = false)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (authenticated && _token is null)
            {
                await LoginAsync(cancellationToken).ConfigureAwait(false);
            }

            HttpRequestMessage request = build();
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw Translate(ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HostUnreachableException("The host did not respond in time.", ex);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && authenticated && attempt == 0)
            {
                response.Dispose();
                _token = null; // session expired or the host restarted: log in again once
                continue;
            }

            CheckProtocol(response);

            if (response.IsSuccessStatusCode
                || (allowNotModified && response.StatusCode == HttpStatusCode.NotModified)
                || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            {
                return response;
            }

            HttpStatusCode status = response.StatusCode;
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            response.Dispose();
            throw status switch
            {
                HttpStatusCode.Unauthorized => new AuthFailedException(),
                HttpStatusCode.TooManyRequests => new LockedOutException(retryAfter ?? TimeSpan.FromMinutes(1)),
                _ => new HostUnreachableException($"The host answered {(int)status}."),
            };
        }
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is not null)
            {
                return; // another caller logged in while we waited
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync("/v1/session", new SessionRequest(_password), Json, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw Translate(ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HostUnreachableException("The host did not respond in time.", ex);
            }

            using (response)
            {
                CheckProtocol(response);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new LockedOutException(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new AuthFailedException();
                }

                response.EnsureSuccessStatusCode();
                _token = (await response.Content.ReadFromJsonAsync<SessionResponse>(Json, cancellationToken).ConfigureAwait(false))!.Token;
            }
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private Exception Translate(HttpRequestException ex)
    {
        // The pinned handler rejects a different certificate by failing validation; report that as the
        // security event it is, not as "unreachable".
        if (_handler.RejectedFingerprint is { } actual)
        {
            return new CertificateMismatchException(_handler.PinnedFingerprint, actual);
        }

        return ex.InnerException is AuthenticationException
            ? new HostUnreachableException("The secure connection to the host failed.", ex)
            : new HostUnreachableException("Could not reach the host.", ex);
    }

    private static void CheckProtocol(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(ProtocolVersion.HeaderName, out var values)
            && int.TryParse(values.FirstOrDefault(), out int hostVersion)
            && hostVersion != ProtocolVersion.Current)
        {
            throw new ProtocolMismatchException(hostVersion, ProtocolVersion.Current);
        }
    }

    /// <summary>Accepts a certificate only if its SHA-256 matches the pin; remembers what a rejected host actually presented.</summary>
    private sealed class PinnedHandler : HttpClientHandler
    {
        public PinnedHandler(string pinnedFingerprint)
        {
            PinnedFingerprint = pinnedFingerprint.ToUpperInvariant();
            ServerCertificateCustomValidationCallback = Validate;
        }

        public string PinnedFingerprint { get; }

        /// <summary>The fingerprint of the most recent certificate that failed the pin, or null.</summary>
        public string? RejectedFingerprint { get; private set; }

        private bool Validate(HttpRequestMessage _, X509Certificate2? cert, X509Chain? __, System.Net.Security.SslPolicyErrors ___)
        {
            if (cert is null)
            {
                return false;
            }

            byte[] actual = SHA256.HashData(cert.GetRawCertData());
            string actualHex = Convert.ToHexString(actual);
            bool match = CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(PinnedFingerprint));
            RejectedFingerprint = match ? null : actualHex;
            return match;
        }
    }
}
