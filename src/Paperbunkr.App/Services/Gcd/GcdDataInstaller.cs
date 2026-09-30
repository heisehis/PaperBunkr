using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Paperbunkr.Data;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.Services.Gcd;

/// <summary>A download that can't be installed (wrong size or hash, not a data file this version reads). The message is for the user.</summary>
public sealed class GcdInstallException(string message) : Exception(message);

/// <summary>
/// Installs, updates and removes the Grand Comics Database extract (docs/superpowers/specs/2026-09-27-gcd-data-design.md §2). The
/// manifest (<c>gcd-data.json</c> at the repo root) names the release asset and its SHA-256; the zip is downloaded next to the data
/// folder, checked, unpacked into a staging folder, opened once to check its schema, and only then swapped in.
/// </summary>
public sealed class GcdDataInstaller
{
    /// <summary>The manifest in the data repo (<see cref="ProjectLinks.GcdDataRepository"/>), so new data can be published without an app release.</summary>
    public const string DefaultManifestUrl = "https://raw.githubusercontent.com/heisehis/paperbunkr-gcd-data/main/gcd-data.json";

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr (+https://github.com/heisehis/PaperBunkr)");
        return http;
    });

    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    public GcdDataInstaller(HttpClient? http = null, string? folder = null, string? manifestUrl = null)
    {
        _http = http ?? SharedHttp.Value;
        Folder = folder ?? Path.GetDirectoryName(GcdDataStore.DefaultPath)!;
        _manifestUrl = manifestUrl ?? DefaultManifestUrl;
    }

    /// <summary><c>%AppData%/Paperbunkr/gcd</c> unless a test says otherwise.</summary>
    public string Folder { get; }

    public string ExtractPath => Path.Combine(Folder, GcdExtractor.ExtractFileName);

    public bool IsInstalled => File.Exists(ExtractPath);

    /// <summary>The installed data's dump date ("2026-09-15"), or null when nothing usable is installed.</summary>
    public string? InstalledDumpDate()
    {
        using var store = GcdDataStore.TryOpen(ExtractPath);
        return store?.DumpDate;
    }

    /// <summary>True when <paramref name="manifest"/> is newer data than <paramref name="installedDumpDate"/> (dates compare as text).</summary>
    public static bool IsNewer(GcdManifest manifest, string? installedDumpDate) =>
        installedDumpDate is null || string.CompareOrdinal(manifest.DumpDate, installedDumpDate) > 0;

    /// <summary>The manifest built into this version of the app (the repo's <c>gcd-data.json</c> when it was built), if any.</summary>
    public static GcdManifest? BundledManifest()
    {
        try
        {
            using var stream = typeof(GcdDataInstaller).Assembly.GetManifestResourceStream("gcd-data.json");
            return stream is null ? null : Validate(JsonSerializer.Deserialize<GcdManifest>(stream));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The published manifest; null when there isn't one yet, it's for another schema, or the network is unavailable.</summary>
    public async Task<GcdManifest?> FetchManifestAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(_manifestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return Validate(await response.Content.ReadFromJsonAsync<GcdManifest>(cancellationToken).ConfigureAwait(false));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static GcdManifest? Validate(GcdManifest? manifest) =>
        manifest is { SchemaVersion: GcdExtractor.SchemaVersion, SizeBytes: > 0, Sha256.Length: 64, DumpDate.Length: > 0 }
        && Uri.TryCreate(manifest.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? manifest
            : null;

    /// <summary>Downloads, checks and swaps in the data <paramref name="manifest"/> describes. Throws <see cref="GcdInstallException"/> on a bad download.</summary>
    public async Task InstallAsync(GcdManifest manifest, IProgress<(long Done, long Total)>? progress, CancellationToken cancellationToken)
    {
        string parent = Path.GetDirectoryName(Folder)!;
        Directory.CreateDirectory(parent);
        string download = Folder + ".download.zip";
        string staging = Folder + ".staging";
        string old = Folder + ".old";

        try
        {
            using (var response = await _http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new GcdInstallException($"The download failed ({(int)response.StatusCode}). The data may not be published yet.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(download);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    if (done > manifest.SizeBytes)
                    {
                        throw new GcdInstallException("The download is bigger than expected, so it wasn't installed.");
                    }

                    progress?.Report((done, manifest.SizeBytes));
                }
            }

            if (new FileInfo(download).Length != manifest.SizeBytes)
            {
                throw new GcdInstallException("The download was incomplete, so it wasn't installed. Try again.");
            }

            string sha;
            await using (var stream = File.OpenRead(download))
            {
                sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }

            if (!sha.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new GcdInstallException("The download didn't match its checksum, so it wasn't installed. Try again.");
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            string staged = Path.Combine(staging, GcdExtractor.ExtractFileName);
            try
            {
                using var zip = ZipFile.OpenRead(download);
                var entry = zip.GetEntry(GcdExtractor.ExtractFileName)
                            ?? throw new GcdInstallException("The download doesn't contain the Grand Comics Database data.");
                entry.ExtractToFile(staged, overwrite: true);
            }
            catch (InvalidDataException)
            {
                throw new GcdInstallException("The download isn't a readable zip file, so it wasn't installed.");
            }

            using (var check = GcdDataStore.TryOpen(staged))
            {
                if (check is null)
                {
                    throw new GcdInstallException("The downloaded data is for a different version of Paperbunkr, so it wasn't installed.");
                }
            }

            SqliteConnection.ClearAllPools();
            if (Directory.Exists(old))
            {
                Directory.Delete(old, recursive: true);
            }

            if (Directory.Exists(Folder))
            {
                Directory.Move(Folder, old);
            }

            Directory.Move(staging, Folder);
            TryDeleteDirectory(old);
        }
        finally
        {
            TryDeleteFile(download);
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>Deletes the data and forgets every GCD id and GCD-only series relation.</summary>
    public void Remove(Func<PaperbunkrDbContext> contextFactory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }

        GcdMatcher.ClearAll(contextFactory);
        GcdBondSync.Run(contextFactory, store: null);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
