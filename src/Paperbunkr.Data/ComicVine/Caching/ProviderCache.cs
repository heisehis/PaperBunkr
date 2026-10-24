using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Paperbunkr.Data.ComicVine.Caching;

/// <summary>What kind of provider answer an entry holds, and so how long it stays fresh.</summary>
public enum ProviderCacheKind
{
    SeriesSearch,
    VolumeDetails,
    VolumeIssueList,
    IssueDetails,
    CoverHash,
}

/// <summary>
/// Provider answers kept on disk so a re-scrape, a retry or the next book of the same series costs no request
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md section 3). One JSON file per entry under
/// <c>provider-cache/&lt;provider&gt;/&lt;kind&gt;/</c> beside the database, plus an in-memory copy for the running session.
/// <para>
/// Files rather than the table the spec first named: a cache must be disposable, so it lives outside the database, needs no migration, and
/// "Refresh provider data" is deleting a folder. An expired entry is still handed back by <see cref="TryGetStale{T}"/>, for when the
/// provider is cooling off or out of quota.
/// </para>
/// </summary>
public sealed class ProviderCache
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _root;
    private readonly Func<DateTime> _clock;
    private readonly ConcurrentDictionary<string, Entry> _memory = new(StringComparer.Ordinal);

    public ProviderCache(string root, Func<DateTime>? clock = null)
    {
        _root = root;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The app's cache, set once at startup; null (tests, tools) means nothing is cached.</summary>
    public static ProviderCache? Shared { get; set; }

    public string Root => _root;

    public static TimeSpan Lifetime(ProviderCacheKind kind) => kind switch
    {
        ProviderCacheKind.SeriesSearch => TimeSpan.FromHours(24),
        ProviderCacheKind.CoverHash => TimeSpan.MaxValue,
        _ => TimeSpan.FromDays(7),
    };

    public bool TryGet<T>(string provider, ProviderCacheKind kind, string key, out T? value)
    {
        value = default;
        if (!TryRead(provider, kind, key, out var entry))
        {
            return false;
        }

        var lifetime = Lifetime(kind);
        if (lifetime != TimeSpan.MaxValue && _clock() - entry.FetchedAtUtc > lifetime)
        {
            return false;
        }

        return TryDeserialize(entry.Payload, out value);
    }

    /// <summary>Any entry, fresh or not: the fallback when the provider can't be asked right now.</summary>
    public bool TryGetStale<T>(string provider, ProviderCacheKind kind, string key, out T? value)
    {
        value = default;
        return TryRead(provider, kind, key, out var entry) && TryDeserialize(entry.Payload, out value);
    }

    public void Set<T>(string provider, ProviderCacheKind kind, string key, T value)
    {
        var entry = new Entry(_clock(), JsonSerializer.Serialize(value, Json));
        _memory[MemoryKey(provider, kind, key)] = entry;
        try
        {
            string path = PathFor(provider, kind, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entry, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The in-memory copy still serves this session.
        }
    }

    /// <summary>"Refresh provider data": forgets everything (cover hashes too). Returns how many entries were removed.</summary>
    public int Clear()
    {
        _memory.Clear();
        int removed = 0;
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    /// <summary>Deletes expired entries (startup upkeep). Cover hashes never expire.</summary>
    public int PruneExpired()
    {
        int removed = 0;
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        foreach (ProviderCacheKind kind in Enum.GetValues<ProviderCacheKind>())
        {
            var lifetime = Lifetime(kind);
            if (lifetime == TimeSpan.MaxValue)
            {
                continue;
            }

            foreach (var providerDir in Directory.EnumerateDirectories(_root))
            {
                string kindDir = Path.Combine(providerDir, kind.ToString());
                if (!Directory.Exists(kindDir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(kindDir, "*.json"))
                {
                    try
                    {
                        if (_clock() - File.GetLastWriteTimeUtc(file) > lifetime)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        return removed;
    }

    /// <summary>(entries, bytes) on disk, for Preferences.</summary>
    public (int Count, long Bytes) Size()
    {
        if (!Directory.Exists(_root))
        {
            return (0, 0);
        }

        int count = 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                bytes += new FileInfo(file).Length;
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return (count, bytes);
    }

    private bool TryRead(string provider, ProviderCacheKind kind, string key, out Entry entry)
    {
        string memoryKey = MemoryKey(provider, kind, key);
        if (_memory.TryGetValue(memoryKey, out entry!))
        {
            return true;
        }

        try
        {
            string path = PathFor(provider, kind, key);
            if (!File.Exists(path))
            {
                return false;
            }

            var read = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), Json);
            if (read is null)
            {
                return false;
            }

            entry = read;
            _memory[memoryKey] = read;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool TryDeserialize<T>(string payload, out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(payload, Json);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static string MemoryKey(string provider, ProviderCacheKind kind, string key) => $"{provider}\n{kind}\n{key}";

    private string PathFor(string provider, ProviderCacheKind kind, string key)
    {
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_root, Sanitize(provider), kind.ToString(), hash + ".json");
    }

    private static string Sanitize(string provider) =>
        string.Concat(provider.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private sealed record Entry(DateTime FetchedAtUtc, string Payload);
}
