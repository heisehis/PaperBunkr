using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generates cover thumbnails for Books (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3), mirroring <see cref="CoverThumbnailService"/>
/// for comics. EPUB covers come straight from the book's own embedded cover image
/// (<see cref="EpubBookSource"/>); PDF reuses the <see cref="PageImageDecoder"/> page-0 pipeline.
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
    /// </summary>
    public bool TryGenerateThumbnail(int bookId, string filePath, BookFormat format)
    {
        string stem = CoverFingerprint.Stem(bookId, filePath, null);
        string destPath = BookCoverThumbnailPaths.GetCachePath(stem);
        if (File.Exists(destPath))
        {
            return true;
        }

        bool ok = format == BookFormat.Epub
            ? TryGenerateFromEpubCover(filePath, destPath)
            : TryGenerateFromPdfFirstPage(filePath, destPath);

        if (ok)
        {
            SweepStaleSiblings(bookId, keepStem: stem);
        }

        return ok;
    }

    private static bool TryGenerateFromEpubCover(string filePath, string destPath)
    {
        try
        {
            using var epub = new EpubBookSource(filePath);
            byte[]? coverBytes = epub.Metadata.CoverImageBytes;
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
