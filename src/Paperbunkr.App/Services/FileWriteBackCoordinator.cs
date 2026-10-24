using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services;

/// <summary>
/// Bridges <see cref="MetadataFileWriteBackService"/> and <see cref="LiveFolderWatchService"/> so
/// the app doesn't flag its own comic files missing when it rewrites their embedded
/// <c>ComicInfo.xml</c>.
///
/// Write-back updates a <c>.cbz</c> by copying it to a <c>.pbwrite-*.tmp</c> sibling and
/// <see cref="System.IO.File.Replace(string,string,string?)"/>-ing it back. On Windows that churn
/// makes a watched folder's <see cref="System.IO.FileSystemWatcher"/> raise <c>Deleted</c>/<c>Created</c>
/// for the real file, which <see cref="LiveFolderWatchService"/> would otherwise read as "the file
/// disappeared" and set <c>Issue.FileIsMissing = true</c>. The watcher checks
/// <see cref="IsSuppressed"/> before acting on any path, and skips <c>.pbwrite-*</c> scratch files
/// outright.
/// </summary>
public static class FileWriteBackCoordinator
{
    /// <summary>Substring marking <see cref="MetadataFileWriteBackService"/>'s scratch copies.</summary>
    public const string ScratchMarker = ".pbwrite-";

    /// <summary>
    /// Windows' <c>ReplaceFile</c> (behind <see cref="System.IO.File.Replace(string,string,string?)"/>)
    /// renames the destination aside to a <c>&lt;name&gt;~RF&lt;hex&gt;.TMP</c> backup for a few
    /// milliseconds, then deletes it. A <see cref="System.IO.FileSystemWatcher"/> reports that as a
    /// <c>Renamed</c> event, which the folder watcher would otherwise follow - repointing the issue's
    /// <c>FilePath</c> at a file that's about to vanish. Confirmed by direct observation 2026-09-05.
    /// </summary>
    private static readonly Regex ReplaceFileBackup = new(@"~RF[0-9a-fA-F]+\.TMP$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, DateTime> SuppressedUntilUtc =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ignore watcher events for <paramref name="path"/> for <paramref name="window"/> -
    /// long enough to cover the copy + zip update + replace plus the watcher's own debounce.</summary>
    public static void Suppress(string path, TimeSpan window)
    {
        if (!string.IsNullOrEmpty(path))
        {
            SuppressedUntilUtc[path] = DateTime.UtcNow + window;
        }
    }

    public static bool IsSuppressed(string path)
    {
        if (path.IndexOf(ScratchMarker, StringComparison.Ordinal) >= 0 || ReplaceFileBackup.IsMatch(path))
        {
            return true;
        }

        if (!SuppressedUntilUtc.TryGetValue(path, out var until))
        {
            return false;
        }

        if (DateTime.UtcNow >= until)
        {
            SuppressedUntilUtc.TryRemove(path, out _);
            return false;
        }

        return true;
    }

    // Files a write-back is rewriting right now, with what to complete when it is done. A count, because two writes of the
    // same file can overlap (a manual write while an automatic one runs).
    private static readonly ConcurrentDictionary<string, (int Count, TaskCompletionSource Done)> Writing =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object WritingGate = new();

    /// <summary>How long the organizer waits for a write to finish before moving the file anyway.</summary>
    public static TimeSpan SettleTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Marks <paramref name="path"/> as being rewritten until the returned handle is disposed.</summary>
    public static IDisposable BeginWrite(string path)
    {
        string key = Normalize(path);
        lock (WritingGate)
        {
            Writing.AddOrUpdate(key,
                _ => (1, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
                (_, existing) => (existing.Count + 1, existing.Done));
        }

        return new WriteHandle(key);
    }

    public static bool IsWriting(string path) => Writing.ContainsKey(Normalize(path));

    /// <summary>
    /// Completes when no metadata write is rewriting <paramref name="path"/> (at once when none is), or after <see cref="SettleTimeout"/>.
    /// The organizer awaits this before each move (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md).
    /// </summary>
    public static async Task WaitForSettledAsync(string path, CancellationToken cancellationToken)
    {
        if (!Writing.TryGetValue(Normalize(path), out var entry))
        {
            return;
        }

        try
        {
            await entry.Done.Task.WaitAsync(SettleTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A stuck write must not stall an organize forever; the write itself fails cleanly if the file moves under it.
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return path;
        }
    }

    private sealed class WriteHandle(string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (WritingGate)
            {
                if (!Writing.TryGetValue(key, out var entry))
                {
                    return;
                }

                if (entry.Count > 1)
                {
                    Writing[key] = (entry.Count - 1, entry.Done);
                    return;
                }

                Writing.TryRemove(key, out _);
                entry.Done.TrySetResult();
            }
        }
    }

    /// <summary>Test hook - drop all suppression windows.</summary>
    internal static void Reset()
    {
        SuppressedUntilUtc.Clear();
        Writing.Clear();
    }
}
