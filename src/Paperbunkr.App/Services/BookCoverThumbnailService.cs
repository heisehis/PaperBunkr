using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generates cover thumbnails for Books (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3, extended for FB2/MOBI by docs/superpowers/specs/
/// 2026-09-01-books-format-ingestion-fb2-mobi-design.md), mirroring <see cref="CoverThumbnailService"/>
/// for comics. Reflowable-format covers come straight from the book's own embedded cover image (via
/// <see cref="BookTextSourceFactory"/>); PDF has no equivalent embedded-cover concept, so its cover
/// reuses the same <see cref="PageImageDecoder"/> pipeline the comic library already uses for PDF
/// page rendering - PDF is already a supported comic archive format there, just decoding page 0.
///
/// <para>
/// Cache files are named <c>{bookId}-{fingerprint}.jpg</c> where the fingerprint is the book's
/// current file path (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-
/// design.md) - path-only, since Books have no persisted size. A rebuild that reassigns
/// <c>Book.Id</c> can no longer serve the previous book's cover for a reused id.
/// </para>
/// </summary>
public class BookCoverThumbnailService
{
    private const int ThumbnailLongestEdge = 400;
    private const int JpegQuality = 85;

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public BookCoverThumbnailService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BookCoverThumbnailService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Generates and caches <paramref name="bookId"/>'s cover at its fingerprinted path, sweeping
    /// any stale <c>{bookId}-*.jpg</c> sibling. Returns false (without throwing) for a
    /// missing/unsupported/corrupt file or a book with no embedded cover - callers treat that as
    /// "skip, try again next run", same contract as <see cref="CoverThumbnailService.TryGenerateThumbnail"/>.
    /// <paramref name="force"/> skips the presence check - see
    /// <see cref="CoverThumbnailService.TryGenerateThumbnail"/>'s doc comment for why.
    /// </summary>
    public bool TryGenerateThumbnail(int bookId, string filePath, BookFormat format, bool force = false)
    {
        string stem = CoverFingerprint.Stem(bookId, filePath, null);
        string destPath = BookCoverThumbnailPaths.GetCachePath(stem);
        if (!force && File.Exists(destPath))
        {
            return true;
        }

        bool ok = format == BookFormat.Pdf
            ? TryGenerateFromPdfFirstPage(filePath, destPath)
            : TryGenerateFromReflowSourceCover(format, filePath, destPath);

        if (ok)
        {
            SweepStaleSiblings(bookId, keepStem: stem);
        }

        return ok;
    }

    private static bool TryGenerateFromReflowSourceCover(BookFormat format, string filePath, string destPath)
    {
        try
        {
            using var source = BookTextSourceFactory.Create(format, filePath);
            byte[]? coverBytes = source.Metadata.CoverImageBytes;
            if (coverBytes is null || coverBytes.Length == 0)
            {
                return false;
            }

            using var stream = new MemoryStream(coverBytes);
            using var bitmap = new Bitmap(stream);
            return ScaleAndSave(bitmap, destPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGenerateFromPdfFirstPage(string filePath, string destPath)
    {
        // The decoder must stay alive for the whole scale+save below - GetPage(0) returns a
        // Bitmap owned by the decoder's own internal cache (same contract PageImageDecoder gives
        // CoverThumbnailService), not a bitmap this method owns and could dispose independently.
        using var decoder = PageImageDecoder.TryOpen(filePath);
        if (decoder is null)
        {
            return false;
        }

        try
        {
            Bitmap page = decoder.GetPage(0);
            return ScaleAndSave(page, destPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Overrides <paramref name="bookId"/>'s displayed cover with a user-picked local image
    /// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md), regardless of format -
    /// a PDF book has no auto cover at all, so this is the only way it gets one. Mirrors
    /// <see cref="CoverThumbnailService.TrySetCustomCover"/>: writes to the book's <b>current</b>
    /// fingerprinted stem (resolved from the database, docs/superpowers/specs/2026-08-27-cover-
    /// thumbnail-identity-validation-design.md), always overwrites (unlike
    /// <see cref="TryGenerateThumbnail"/>'s presence check), sweeps any stale sibling, and uses
    /// <see cref="BookCoverImageCache.InvalidateMemoryOnly"/> - the file-deleting
    /// <see cref="BookCoverImageCache.Invalidate"/> would wipe the very file just written.
    /// </summary>
    public bool TrySetCustomCover(int bookId, string sourceImagePath)
    {
        string stem = StemFor(bookId);
        string destPath = BookCoverThumbnailPaths.GetCachePath(stem);
        try
        {
            using var source = new Bitmap(sourceImagePath);
            if (!ScaleAndSave(source, destPath))
            {
                return false;
            }

            SweepStaleSiblings(bookId, keepStem: stem);
            BookCoverImageCache.InvalidateMemoryOnly(stem);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reverts a custom cover: deletes every cached file for this id (real
    /// <see cref="BookCoverImageCache.Invalidate"/>) and regenerates the auto cover. EPUB gets its
    /// embedded cover back immediately; PDF has no auto cover, so it simply goes blank - the honest
    /// reset for that format.
    /// </summary>
    public void ResetCover(int bookId, string? filePath, BookFormat format)
    {
        BookCoverImageCache.Invalidate(bookId);
        if (!string.IsNullOrEmpty(filePath))
        {
            TryGenerateThumbnail(bookId, filePath, format);
        }
    }

    private string StemFor(int bookId)
    {
        using var context = _contextFactory();
        var book = context.Books
            .Where(b => b.Id == bookId)
            .Select(b => new { b.FilePath })
            .FirstOrDefault();
        return CoverFingerprint.Stem(bookId, book?.FilePath, null);
    }

    private static bool ScaleAndSave(Bitmap source, string destPath)
    {
        var size = source.PixelSize;
        int longest = Math.Max(size.Width, size.Height);
        if (longest <= 0)
        {
            return false;
        }

        double scale = Math.Min(1.0, (double)ThumbnailLongestEdge / longest);
        var target = new PixelSize(
            Math.Max(1, (int)Math.Round(size.Width * scale)),
            Math.Max(1, (int)Math.Round(size.Height * scale)));

        using Bitmap scaled = source.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        scaled.Save(destPath, new JpegBitmapEncoderOptions { Quality = JpegQuality });
        return true;
    }

    /// <summary>
    /// Generates thumbnails for every Book with no cached thumbnail for its current fingerprint,
    /// then deletes orphaned cache files whose stem no longer matches any current book. Presence-
    /// based, one bad file doesn't stop the batch - same contract as
    /// <see cref="CoverThumbnailService.GenerateAllAsync"/>.
    /// </summary>
    public async Task GenerateAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var all = context.Books
                    .Select(b => new { b.Id, b.FilePath, b.Format })
                    .ToList();

                var validStems = new HashSet<string>(
                    all.Select(b => CoverFingerprint.Stem(b.Id, b.FilePath, null)),
                    StringComparer.Ordinal);

                var candidates = all
                    .Where(b => !File.Exists(BookCoverThumbnailPaths.GetCachePath(
                        CoverFingerprint.Stem(b.Id, b.FilePath, null))))
                    .ToList();

                int total = candidates.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        TryGenerateThumbnail(candidate.Id, candidate.FilePath, candidate.Format);
                    }
                    catch
                    {
                        // One bad file doesn't stop the batch.
                    }

                    progress.Report((++done, total));
                }

                CollectOrphans(validStems);
            },
            ct);
    }

    /// <summary>
    /// Unconditionally re-derives every Book's cover from its source file and overwrites the
    /// cache, then runs the same orphan GC as <see cref="GenerateAllAsync"/> - mirrors
    /// <see cref="CoverThumbnailService.VerifyAllAsync"/>'s doc comment for the full rationale
    /// (docs/superpowers/specs/2026-08-30-cover-thumbnail-content-verification-design.md).
    /// </summary>
    public async Task VerifyAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var all = context.Books
                    .Select(b => new { b.Id, b.FilePath, b.Format })
                    .ToList();

                var validStems = new HashSet<string>(
                    all.Select(b => CoverFingerprint.Stem(b.Id, b.FilePath, null)),
                    StringComparer.Ordinal);

                int total = all.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var candidate in all)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        TryGenerateThumbnail(candidate.Id, candidate.FilePath, candidate.Format, force: true);
                    }
                    catch
                    {
                        // One bad file doesn't stop the batch.
                    }

                    progress.Report((++done, total));
                }

                CollectOrphans(validStems);
            },
            ct);
    }

    private static void SweepStaleSiblings(int bookId, string keepStem)
    {
        string keepName = keepStem + ".jpg";
        foreach (string path in BookCoverThumbnailPaths.EnumerateForBook(bookId).ToList())
        {
            if (!string.Equals(Path.GetFileName(path), keepName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static void CollectOrphans(HashSet<string> validStems)
    {
        foreach (string path in BookCoverThumbnailPaths.EnumerateAll().ToList())
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!validStems.Contains(stem))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
