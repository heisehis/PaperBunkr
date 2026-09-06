using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services.Covers;

/// <summary>
/// The cover-cache "attic": a soft-delete holding area
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// The orphan GC and the rebuild purge move files here instead of deleting them, so a mistaken
/// sweep - or a file whose owning row comes back (a healed path, a restored DB) - is recoverable.
///
/// Attic file names are <c>{id}.{ticks}.jpg</c>. Pruned by age (14 days) then by total size
/// (500 MB, oldest-out). All operations swallow <see cref="IOException"/> /
/// <see cref="UnauthorizedAccessException"/> - several app instances can share one cache.
/// </summary>
public static class CoverCacheAttic
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    public const long MaxSizeBytes = 500L * 1024 * 1024;

    /// <summary>Move <paramref name="cacheFilePath"/> into <paramref name="atticDir"/> as <c>{originalName}.{ticks}.jpg</c>.</summary>
    public static void MoveToAttic(string cacheFilePath, string atticDir)
    {
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return;
            }

            Directory.CreateDirectory(atticDir);
            string stem = Path.GetFileNameWithoutExtension(cacheFilePath);
            string dest = Path.Combine(atticDir, $"{stem}.{DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}.jpg");
            File.Move(cacheFilePath, dest, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Move every current cover file out of <paramref name="cacheDir"/> into <paramref name="atticDir"/>
    /// (used by the library-rebuild purge). Only touches <c>*.jpg</c> directly in the folder, never
    /// the attic subfolder itself.
    /// </summary>
    public static void AtticEverything(string cacheDir, string atticDir)
    {
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(cacheDir, "*.jpg"))
            {
                MoveToAttic(path, atticDir);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Delete attic entries older than <see cref="MaxAge"/>, then oldest-first until under <see cref="MaxSizeBytes"/>.</summary>
    public static void Prune(string atticDir)
    {
        try
        {
            if (!Directory.Exists(atticDir))
            {
                return;
            }

            var files = Directory.GetFiles(atticDir, "*.jpg")
                .Select(p => new FileInfo(p))
                .OrderBy(fi => fi.LastWriteTimeUtc)
                .ToList();

            DateTime cutoff = DateTime.UtcNow - MaxAge;
            foreach (var fi in files.ToList())
            {
                if (fi.LastWriteTimeUtc < cutoff)
                {
                    TryDelete(fi);
                    files.Remove(fi);
                }
            }

            long total = files.Sum(fi => SafeLength(fi));
            foreach (var fi in files)
            {
                if (total <= MaxSizeBytes)
                {
                    break;
                }

                total -= SafeLength(fi);
                TryDelete(fi);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// If the attic holds a file for <paramref name="id"/>, move the newest one back to
    /// <c>{cacheDir}/{id}.jpg</c> (unless a live file is already there). Returns true when a cover
    /// was restored - the caller can then skip a re-decode.
    /// </summary>
    public static bool TryRestoreById(int id, string cacheDir, string atticDir)
    {
        try
        {
            string dest = Path.Combine(cacheDir, $"{id.ToString(CultureInfo.InvariantCulture)}.jpg");
            if (File.Exists(dest) || !Directory.Exists(atticDir))
            {
                return File.Exists(dest);
            }

            string prefix = id.ToString(CultureInfo.InvariantCulture) + ".";
            var newest = Directory.GetFiles(atticDir, "*.jpg")
                .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)
                            && IsAtticFileForId(Path.GetFileName(p), id))
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null)
            {
                return false;
            }

            Directory.CreateDirectory(cacheDir);
            File.Move(newest.FullName, dest, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // "{id}.{ticks}.jpg" -> the first dot-segment must parse as exactly this id.
    private static bool IsAtticFileForId(string fileName, int id)
    {
        int firstDot = fileName.IndexOf('.');
        if (firstDot <= 0)
        {
            return false;
        }

        return int.TryParse(fileName.AsSpan(0, firstDot), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
               && parsed == id;
    }

    private static long SafeLength(FileInfo fi)
    {
        try
        {
            return fi.Exists ? fi.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static void TryDelete(FileInfo fi)
    {
        try
        {
            if (fi.Exists)
            {
                fi.Delete();
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
