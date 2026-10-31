using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services.Dictionary;

/// <summary>
/// Downloads Princeton WordNet 3.0 on first use and turns it into the local dictionary file
/// (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md §2). WordNet is free to use with its licence notice kept (see About > Legal);
/// the archive is only a build input and is not kept.
/// </summary>
public sealed class DictionaryDataService
{
    public const string DefaultUrl = "https://wordnetcode.princeton.edu/3.0/WNdb-3.0.tar.gz";

    private readonly string _path;
    private readonly Func<HttpClient> _http;
    private readonly string _url;

    public DictionaryDataService(string? databasePath = null, Func<HttpClient>? http = null, string? url = null)
    {
        _path = databasePath ?? Path.Combine(AppDataPaths.LocalRoot, "Dictionary", "dictionary-en.db");
        _http = http ?? (() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        _url = url ?? DefaultUrl;
    }

    public string DatabasePath => _path;

    public bool IsInstalled => File.Exists(_path);

    private static readonly (char Pos, string File)[] PosFiles = [('n', "noun"), ('v', "verb"), ('a', "adj"), ('r', "adv")];

    public async Task DownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var client = _http();
        using var response = await client.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        var archive = new MemoryStream();
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                archive.Write(buffer, 0, read);
                if (total is > 0)
                {
                    progress?.Report(0.8 * archive.Length / total.Value);
                }
            }
        }

        archive.Position = 0;
        await Task.Run(() => BuildFromArchive(archive, progress), ct).ConfigureAwait(false);
        progress?.Report(1);
    }

    /// <summary>Test seam and the second half of the download: a WordNet <c>.tar.gz</c> in, the dictionary file out (written beside the target, then moved into place).</summary>
    internal void BuildFromArchive(Stream tarGz, IProgress<double>? progress = null)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var gzip = new GZipStream(tarGz, CompressionMode.Decompress))
        using (var tar = new TarReader(gzip))
        {
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.DataStream is null || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                {
                    continue;
                }

                using var text = new StreamReader(entry.DataStream, Encoding.UTF8);
                files[Path.GetFileName(entry.Name)] = text.ReadToEnd();
            }
        }

        var entries = new List<DictionaryEntry>();
        var exceptions = new List<(string, string)>();
        foreach (var (pos, name) in PosFiles)
        {
            if (!files.TryGetValue("data." + name, out string? data) || !files.TryGetValue("index." + name, out string? index))
            {
                throw new InvalidDataException($"The WordNet archive has no data/index files for '{name}'.");
            }

            entries.AddRange(WordNetParser.ParsePos(pos, data, index));
            if (files.TryGetValue(name + ".exc", out string? exc))
            {
                exceptions.AddRange(WordNetParser.ParseExceptions(exc));
            }
        }

        progress?.Report(0.9);
        string temp = _path + ".building";
        DictionaryDatabase.Build(temp, entries, exceptions);
        File.Move(temp, _path, overwrite: true);
    }
}
