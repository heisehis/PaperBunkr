using System;
using System.IO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Derives a stable identity token ("stem") for a cached cover thumbnail
/// (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md).
///
/// <para>
/// The cover cache used to be keyed purely by the auto-increment <c>Issue.Id</c>/<c>Book.Id</c>,
/// so any library rebuild that reassigned primary keys silently served the previous entity's
/// cover for a reused id. The stem folds the entity's <b>current file identity</b> into the
/// cache-file name (<c>{id}-{hash}.jpg</c>), so a reader only trusts a file whose id <b>and</b>
/// file identity both match - a mismatch just misses and regenerates.
/// </para>
///
/// <para>
/// Identity = normalized full path + file size in bytes. Comics pass <c>Issue.FileSize</c>
/// (persisted); Books have no size column, so they pass <see langword="null"/> and get a
/// path-only stem - still enough to catch id reuse (a different comic lives at a different path).
/// A fileless placeholder entry (no path at all - a deliberate CE deviation the custom-cover
/// feature already supports) gets a fixed <c>{id}-nofile</c> stem so its user-picked cover still
/// has a stable home.
/// </para>
/// </summary>
public static class CoverFingerprint
{
    /// <summary>
    /// Cache-file stem (no extension) for the entity with primary key <paramref name="id"/>
    /// currently backed by <paramref name="filePath"/> (<paramref name="fileSizeBytes"/> bytes,
    /// or <see langword="null"/> when unknown). Deterministic and allocation-cheap - safe to call
    /// per card during a library grid load.
    /// </summary>
    public static string Stem(int id, string? filePath, long? fileSizeBytes)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return $"{id}-nofile";
        }

        string normalized = Normalize(filePath);
        string identity = fileSizeBytes is long size
            ? $"{normalized}|{size}"
            : normalized;

        return $"{id}-{Fnv1a(identity):x8}";
    }

    private static string Normalize(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // A malformed path can't be canonicalized - hash it as-is rather than throwing into a
            // card build. Two entities with the same malformed path still collide identically,
            // which is the property that matters.
            full = path;
        }

        return full.Replace('\\', '/').ToLowerInvariant();
    }

    // FNV-1a, 32-bit - the same stable, non-cryptographic hash SeriesCardSample.StableHash uses
    // (string.GetHashCode() is randomized per process and would change the stem every launch).
    private static uint Fnv1a(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }
}
