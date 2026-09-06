using System.Globalization;

namespace Paperbunkr.App.Services;

/// <summary>
/// Derives the cache-file "stem" (name without extension) for a generated cover thumbnail.
///
/// <para>
/// <b>History.</b> The cache was once keyed by a <c>{id}-{hash(path)}</c> fingerprint
/// (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md) so a library
/// rebuild that reused an <c>Issue.Id</c> couldn't serve the previous entity's cover. That scheme
/// destroyed covers on every routine file-path change (metadata write-back, file moves, the
/// <c>~RF*.TMP</c> watch bug, drive-letter changes), because the orphan GC hard-deleted any file
/// whose fingerprint no longer matched. Root-fixed 2026-09-06
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md): the stem is
/// now just the bare id, the orphan GC only attics files whose id has <b>no</b> row at all, and
/// id-reuse after a rebuild is handled by a single explicit "library was rebuilt" purge
/// (<see cref="Covers.CoverCacheState"/>).
/// </para>
///
/// <para>
/// Kept as a thin shim with its original signature so the ~10 call sites that pass it an issue/book
/// plus its file identity compile unchanged; <paramref name="filePath"/> / <paramref name="fileSizeBytes"/>
/// are now ignored.
/// </para>
/// </summary>
public static class CoverFingerprint
{
    /// <summary>Cache-file stem for the entity with primary key <paramref name="id"/>. The file
    /// identity arguments are ignored (see the type remarks).</summary>
    public static string Stem(int id, string? filePath, long? fileSizeBytes) =>
        id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Recovers the primary-key from a stem produced by <see cref="Stem"/>.</summary>
    public static bool TryGetId(string stem, out int id) =>
        int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
}
