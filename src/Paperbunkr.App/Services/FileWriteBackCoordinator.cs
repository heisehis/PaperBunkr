using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

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

    /// <summary>Test hook - drop all suppression windows.</summary>
    internal static void Reset() => SuppressedUntilUtc.Clear();
}
