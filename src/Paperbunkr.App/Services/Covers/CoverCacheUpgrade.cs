using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services.Covers;

/// <summary>
/// One-time, on-disk migration of the cover caches from the retired <c>{id}-{hash}.jpg</c>
/// fingerprint scheme to the flat <c>{id}.jpg</c> scheme
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// No database involvement. Idempotent - a second run finds nothing to do.
///
/// <para>
/// For each cache directory: legacy files are grouped by their leading id; the newest by write
/// time becomes <c>{id}.jpg</c> and the rest go to the attic (so an accidental keep-the-wrong-one
/// is recoverable). A pre-existing bare <c>{id}.jpg</c> is left in place unless a legacy sibling is
/// newer.
/// </para>
///
/// <para>
/// <b>Known gap:</b> custom covers set before this shipped weren't tracked, so they can't be moved
/// into <see cref="CustomCoverPaths"/> automatically - they are treated as ordinary generated
/// covers (regenerable, and swept on a later rebuild).
/// </para>
/// </summary>
public static class CoverCacheUpgrade
{
    public static void RunOnce()
    {
        var state = CoverCacheState.Read();
        bool alreadyUpgraded = state.SchemaVersion >= CoverCacheState.CurrentSchemaVersion;

        bool flattenedComics = FlattenDirectory(CoverThumbnailPaths.ThumbnailDirectory, CoverThumbnailPaths.AtticDirectory);
        bool flattenedBooks = FlattenDirectory(BookCoverThumbnailPaths.ThumbnailDirectory, BookCoverThumbnailPaths.AtticDirectory);

        if (alreadyUpgraded && !flattenedComics && !flattenedBooks)
        {
            return;
        }

        // Stamp the schema version (RecordCounts seeds a generation + schemaVersion; the real counts
        // are filled in by the next GenerateAllAsync pass).
        CoverCacheState.RecordCounts(state.IssueCount, state.BookCount);
    }

    /// <summary>Returns true if it renamed/attic'd at least one legacy file.</summary>
    private static bool FlattenDirectory(string cacheDir, string atticDir)
    {
        List<string> legacy;
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return false;
            }

            legacy = Directory.GetFiles(cacheDir, "*-*.jpg").ToList();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (legacy.Count == 0)
        {
            return false;
        }

        // group legacy files by the id before the first '-'
        var byId = new Dictionary<int, List<string>>();
        foreach (string path in legacy)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int dash = name.IndexOf('-');
            if (dash <= 0)
            {
                continue;
            }

            if (int.TryParse(name.AsSpan(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                if (!byId.TryGetValue(id, out var list))
                {
                    list = new List<string>();
                    byId[id] = list;
                }

                list.Add(path);
            }
            else
            {
                // Not an id-prefixed file - attic it, it isn't ours to key.
                CoverCacheAttic.MoveToAttic(path, atticDir);
            }
        }

        bool didWork = false;
        foreach (var (id, files) in byId)
        {
            string canonical = Path.Combine(cacheDir, $"{id.ToString(CultureInfo.InvariantCulture)}.jpg");

            var candidates = new List<string>(files);
            if (File.Exists(canonical))
            {
                candidates.Add(canonical);
            }

            string keeper = candidates
                .OrderByDescending(p => SafeWriteTime(p))
                .First();

            foreach (string loser in candidates.Where(p => !string.Equals(p, keeper, StringComparison.OrdinalIgnoreCase)))
            {
                CoverCacheAttic.MoveToAttic(loser, atticDir);
                didWork = true;
            }

            if (!string.Equals(keeper, canonical, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Move(keeper, canonical, overwrite: true);
                    didWork = true;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return didWork;
    }

    private static DateTime SafeWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }
}
