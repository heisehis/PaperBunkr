using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>
/// The bounded on-disk cache of page bytes fetched from a remote library (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §7.3): so re-opening an issue is instant and pages already
/// read stay readable when the host is offline. Originals are stored exactly as received (no re-encode).
/// </summary>
/// <remarks>
/// Bounded two ways so it can never quietly balloon: a byte quota enforced on every write (least-recently
/// used files go first) and a last-use TTL swept by a scheduled task. "Use" is the file's last-write time,
/// touched on every hit - NTFS often has last-access updates switched off, so it can't be trusted. The
/// newest write is never evicted by its own quota pass, so one page larger than the whole quota still
/// works (it simply becomes the only cached page). Layout: <c>{dir}/{sourceId}/{remoteIssueId}/{page}.bin</c>.
/// </remarks>
public sealed class PeerPageCache
{
    public const long DefaultQuotaBytes = 2L * 1024 * 1024 * 1024;

    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromDays(30);

    /// <summary>
    /// The app-wide instance over the default directory. The reader, the removal purge and the scheduled TTL sweep
    /// must all use this one: it keeps a running byte total, which a second instance sweeping the same folder would
    /// leave stale.
    /// </summary>
    public static PeerPageCache Shared { get; } = new(Paperbunkr.Data.AppDataPaths.Combine("remote-library", "pages"));

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private long _totalBytes = -1;

    public PeerPageCache(string directory, long quotaBytes = DefaultQuotaBytes, TimeSpan? timeToLive = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(quotaBytes, 1);
        _directory = directory;
        QuotaBytes = quotaBytes;
        TimeToLive = timeToLive ?? DefaultTimeToLive;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Changeable at runtime (Preferences). Takes effect on the next write.</summary>
    public long QuotaBytes { get; set; }

    public TimeSpan TimeToLive { get; }

    /// <summary>Bytes currently cached.</summary>
    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                EnsureTotal();
                return _totalBytes;
            }
        }
    }

    public bool TryGet(int sourceId, int remoteIssueId, int pageIndex, out byte[]? bytes)
    {
        string path = PathFor(sourceId, remoteIssueId, pageIndex);
        try
        {
            bytes = File.ReadAllBytes(path);
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);   // a hit is a use
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = null;
            return false;
        }
    }

    public bool Contains(int sourceId, int remoteIssueId, int pageIndex) => File.Exists(PathFor(sourceId, remoteIssueId, pageIndex));

    /// <summary>Stores the page atomically (temp file then rename, so a reader never sees half a page), then trims to the quota.</summary>
    public void Put(int sourceId, int remoteIssueId, int pageIndex, byte[] bytes)
    {
        string path = PathFor(sourceId, remoteIssueId, pageIndex);

        lock (_gate)
        {
            // The running total is established (a directory scan) BEFORE this file exists, so the new bytes
            // are added exactly once below - scanning after the write would count them twice.
            EnsureTotal();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            long previous = File.Exists(path) ? new FileInfo(path).Length : 0;
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);

            _totalBytes += bytes.Length - previous;
            TrimToQuota(protect: path);
        }
    }

    /// <summary>Deletes the cached pages of one remote issue (its content changed on the host).</summary>
    public void PurgeIssue(int sourceId, int remoteIssueId)
    {
        string dir = Path.Combine(_directory, sourceId.ToString(), remoteIssueId.ToString());
        lock (_gate)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
            }

            _totalBytes = -1;
        }
    }

    /// <summary>Deletes every cached page of <paramref name="sourceId"/> (the library was removed, or relinked so its ids now mean different books).</summary>
    public void PurgeSource(int sourceId)
    {
        string dir = Path.Combine(_directory, sourceId.ToString());
        lock (_gate)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
            }

            _totalBytes = -1;   // recount on next use
        }
    }

    /// <summary>Deletes pages not used within <see cref="TimeToLive"/> and prunes emptied folders. Returns how many pages went.</summary>
    public int SweepExpired()
    {
        DateTime cutoff = _time.GetUtcNow().UtcDateTime - TimeToLive;
        int removed = 0;
        lock (_gate)
        {
            foreach (FileInfo file in EnumerateFiles())
            {
                if (file.LastWriteTimeUtc < cutoff && TryDelete(file))
                {
                    removed++;
                }
            }

            PruneEmptyDirectories();
            _totalBytes = -1;
        }

        return removed;
    }

    private string PathFor(int sourceId, int remoteIssueId, int pageIndex) =>
        Path.Combine(_directory, sourceId.ToString(), remoteIssueId.ToString(), pageIndex + ".bin");

    private void EnsureTotal()
    {
        if (_totalBytes >= 0)
        {
            return;
        }

        _totalBytes = EnumerateFiles().Sum(f => f.Length);
    }

    private void TrimToQuota(string protect)
    {
        if (_totalBytes <= QuotaBytes)
        {
            return;
        }

        // Oldest use first; never the file that was just written.
        foreach (FileInfo file in EnumerateFiles().OrderBy(f => f.LastWriteTimeUtc))
        {
            if (_totalBytes <= QuotaBytes)
            {
                break;
            }

            if (string.Equals(file.FullName, protect, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long length = file.Length;
            if (TryDelete(file))
            {
                _totalBytes -= length;
            }
        }
    }

    private IEnumerable<FileInfo> EnumerateFiles()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<FileInfo>();
        }

        try
        {
            return new DirectoryInfo(_directory).EnumerateFiles("*.bin", SearchOption.AllDirectories).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void PruneEmptyDirectories()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (string dir in Directory.EnumerateDirectories(_directory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
