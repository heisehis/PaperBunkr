using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Opds;

/// <summary>Basic-auth credentials for one catalog; only ever sent to <see cref="Host"/>.</summary>
public sealed record OpdsCredentials(string Host, string Username, string Password);

/// <summary>
/// Talks to an OPDS catalog (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md §2-§5). Redirects are followed by hand so credentials
/// are dropped the moment a redirect leaves the catalog's host.
/// </summary>
public sealed class OpdsClient
{
    private const int MaxRedirects = 5;

    private readonly Func<HttpClient> _http;

    public OpdsClient(Func<HttpClient>? http = null) =>
        _http = http ?? (() => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) });

    public static OpdsCredentials? CredentialsFor(OpdsCatalog catalog, string? password) =>
        string.IsNullOrWhiteSpace(catalog.Username) || password is null || !Uri.TryCreate(catalog.Url, UriKind.Absolute, out var uri)
            ? null
            : new OpdsCredentials(uri.Host, catalog.Username, password);

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, Uri url, OpdsCredentials? credentials, CancellationToken ct, bool headersOnly)
    {
        for (int hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (credentials is not null && string.Equals(url.Host, credentials.Host, StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials.Username + ":" + credentials.Password)));
            }

            var response = await client.SendAsync(request, headersOnly ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                if (hop >= MaxRedirects)
                {
                    throw new HttpRequestException("The catalog redirected too many times.");
                }

                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw new UnauthorizedAccessException("The catalog asked for a username and password, or rejected the ones saved.");
            }

            response.EnsureSuccessStatusCode();
            return response;
        }
    }

    public async Task<OpdsFeed> GetFeedAsync(Uri url, OpdsCredentials? credentials, CancellationToken ct = default)
    {
        using var client = _http();
        using var response = await SendAsync(client, url, credentials, ct, headersOnly: false).ConfigureAwait(false);
        string xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return OpdsFeedParser.Parse(xml, response.RequestMessage?.RequestUri ?? url);
    }

    /// <summary>A small resource such as a cover thumbnail; the catalog's credentials go along only for its own host.</summary>
    public async Task<byte[]> GetBytesAsync(Uri url, OpdsCredentials? credentials, CancellationToken ct = default)
    {
        using var client = _http();
        using var response = await SendAsync(client, url, credentials, ct, headersOnly: false).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The address to browse for <paramref name="query"/>, or null when the feed offers no search.</summary>
    public async Task<Uri?> ResolveSearchAsync(OpdsFeed feed, string query, OpdsCredentials? credentials, CancellationToken ct = default)
    {
        if (feed.Search is null || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        string template = Uri.UnescapeDataString(feed.Search.OriginalString);
        if (!feed.SearchIsTemplate)
        {
            using var client = _http();
            using var response = await SendAsync(client, feed.Search, credentials, ct, headersOnly: false).ConfigureAwait(false);
            string? found = OpdsFeedParser.ParseOpenSearchTemplate(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (found is null)
            {
                return null;
            }

            template = found;
        }

        string filled = template.Replace("{searchTerms}", Uri.EscapeDataString(query.Trim()), StringComparison.Ordinal);
        // Optional OpenSearch parameters such as {startPage?} are left unfilled.
        filled = System.Text.RegularExpressions.Regex.Replace(filled, @"\{[^}]*\?\}", string.Empty);
        return Uri.TryCreate(feed.Search, filled, out var result) ? result : null;
    }

    /// <summary>The acquisition link to download: EPUB, then FB2, MOBI, PDF; null when none is a book format Paperbunkr reads.</summary>
    public static OpdsLink? ChooseAcquisition(IEnumerable<OpdsLink> acquisitions) =>
        acquisitions.Select(l => (Link: l, Rank: FormatRank(l))).Where(x => x.Rank < int.MaxValue).OrderBy(x => x.Rank).Select(x => x.Link).FirstOrDefault();

    private static int FormatRank(OpdsLink link) => ExtensionFor(link) switch
    {
        ".epub" => 0,
        ".fb2" or ".fb2.zip" => 1,
        ".mobi" or ".azw3" or ".azw" => 2,
        ".pdf" => 3,
        _ => int.MaxValue,
    };

    /// <summary>The file extension for an acquisition link, from its media type and then from its address.</summary>
    public static string? ExtensionFor(OpdsLink link)
    {
        string type = (link.Type ?? string.Empty).ToLowerInvariant();
        if (type.Contains("epub")) return ".epub";
        if (type.Contains("pdf")) return ".pdf";
        if (type.Contains("fb2") || type.Contains("fictionbook")) return type.Contains("zip") ? ".fb2.zip" : ".fb2";
        if (type.Contains("mobi")) return ".mobi";
        if (type.Contains("azw")) return ".azw3";

        string path = Uri.TryCreate(link.Href, UriKind.Absolute, out var uri) ? uri.AbsolutePath.ToLowerInvariant() : link.Href.ToLowerInvariant();
        foreach (string ext in new[] { ".epub", ".fb2.zip", ".fb2", ".mobi", ".azw3", ".azw", ".pdf" })
        {
            if (path.EndsWith(ext, StringComparison.Ordinal))
            {
                return ext;
            }
        }

        return null;
    }

    /// <summary>Streams the book to <c>{name}.part</c> and renames it when complete; a failed or cancelled download leaves nothing behind.</summary>
    public async Task<string> DownloadAsync(OpdsLink link, string folder, string fileNameBase, OpdsCredentials? credentials, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string extension = ExtensionFor(link) ?? throw new InvalidDataException("That link is not a book format Paperbunkr can read.");
        Directory.CreateDirectory(folder);
        string target = UniquePath(folder, SanitizeFileName(fileNameBase), extension);
        string part = target + ".part";
        try
        {
            using var client = _http();
            using var response = await SendAsync(client, new Uri(link.Href), credentials, ct, headersOnly: true).ConfigureAwait(false);
            long? total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dest = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total is > 0)
                    {
                        progress?.Report((double)done / total.Value);
                    }
                }
            }

            File.Move(part, target);
            progress?.Report(1);
            return target;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Length > 120)
        {
            cleaned = cleaned[..120].TrimEnd();
        }

        return cleaned.Length == 0 ? "book" : cleaned;
    }

    private static string UniquePath(string folder, string name, string extension)
    {
        string path = Path.Combine(folder, name + extension);
        for (int n = 2; File.Exists(path) || File.Exists(path + ".part"); n++)
        {
            path = Path.Combine(folder, $"{name} ({n}){extension}");
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
