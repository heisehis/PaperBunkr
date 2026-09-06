using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generates cover thumbnails for Books (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3), mirroring <see cref="CoverThumbnailService"/>
/// for comics. Reflowable-format covers come from the book's embedded cover image; PDF reuses the
/// <see cref="PageImageDecoder"/> page-0 pipeline.
///
/// <para>
/// Cache files are named <c>{bookId}.jpg</c> (docs/superpowers/specs/2026-09-06-scheduled-tasks-
/// and-cover-durability-design.md). A cover is only ever moved to the attic when its id matches no
/// book row; a path change never touches it. User-picked covers live in
/// <see cref="CustomBookCoverPaths"/> and are never swept.
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
    /// Generates and caches <paramref name="bookId"/>'s cover at <c>{bookId}.jpg</c>. Returns false
    /// (without throwing) for a missing/unsupported/corrupt file or a book with no embedded cover.
    /// <paramref name="force"/> skips the presence check.
    /// </summary>
    public bool TryGenerateThumbnail(int bookId, string filePath, BookFormat format, bool force = false)
    {
        string destPath = BookCoverThumbnailPaths.GetCachePath(bookId);

        if (!force)
        {
            if (!File.Exists(destPath))
            {
                CoverCacheAttic.TryRestoreById(bookId, BookCoverThumbnailPaths.ThumbnailDirectory, BookCoverThumbnailPaths.AtticDirectory);
            }

            if (File.Exists(destPath))
            {
                return true;
            }
        }

        return format == BookFormat.Pdf
            ? TryGenerateFromPdfFirstPage(filePath, destPath)
            : TryGenerateFromReflowSourceCover(format, filePath, destPath);
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
    /// Overrides <paramref name="bookId"/>'s displayed cover with a user-picked local image, written
    /// to <see cref="CustomBookCoverPaths"/> - its own directory, never swept. Any generated
    /// <c>{id}.jpg</c> is removed so the custom art wins cleanly.
    /// </summary>
    public bool TrySetCustomCover(int bookId, string sourceImagePath)
    {
        string destPath = CustomBookCoverPaths.GetCachePath(bookId);
        try
        {
            using var source = new Bitmap(sourceImagePath);
            if (!ScaleAndSave(source, destPath))
            {
                return false;
            }

            TryDelete(BookCoverThumbnailPaths.GetCachePath(bookId));
            BookCoverImageCache.InvalidateMemoryOnly(bookId.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reverts a custom cover: deletes the custom + generated file and the in-memory entry, then
    /// regenerates the auto cover. EPUB gets its embedded cover back; PDF goes blank.
    /// </summary>
    public void ResetCover(int bookId, string? filePath, BookFormat format)
    {
        BookCoverImageCache.Invalidate(bookId);
        if (!string.IsNullOrEmpty(filePath))
        {
            TryGenerateThumbnail(bookId, filePath, format);
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

    /// <summary>Generates thumbnails for every Book with no cached cover, then attics files whose id matches no book.</summary>
    public async Task GenerateAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var all = context.Books
                    .Select(b => new { b.Id, b.FilePath, b.Format })
                    .ToList();

                var validIds = new HashSet<int>(all.Select(b => b.Id));

                var candidates = all
                    .Where(b => !File.Exists(BookCoverThumbnailPaths.GetCachePath(b.Id))
                                && !CustomBookCoverPaths.Exists(b.Id))
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

                RunCacheMaintenance(validIds, all.Count);
            },
            ct);
    }

    /// <summary>
    /// Re-derives a Book's cover from source only when the source file changed since the cover was
    /// made (mtime-smart), then runs the same id-based attic + prune.
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

                var validIds = new HashSet<int>(all.Select(b => b.Id));

                int total = all.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var candidate in all)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (NeedsReverify(candidate.Id, candidate.FilePath))
                        {
                            TryGenerateThumbnail(candidate.Id, candidate.FilePath, candidate.Format, force: true);
                        }
                    }
                    catch
                    {
                        // One bad file doesn't stop the batch.
                    }

                    progress.Report((++done, total));
                }

                RunCacheMaintenance(validIds, all.Count);
            },
            ct);
    }

    /// <summary>Regenerates covers only for books with no cover (generated or custom) and a readable source.</summary>
    public async Task RepairMissingAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var missing = context.Books
                    .Select(b => new { b.Id, b.FilePath, b.Format })
                    .ToList()
                    .Where(b => !File.Exists(BookCoverThumbnailPaths.GetCachePath(b.Id))
                                && !CustomBookCoverPaths.Exists(b.Id)
                                && SourceReadable(b.FilePath))
                    .ToList();

                int total = missing.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var book in missing)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        TryGenerateThumbnail(book.Id, book.FilePath, book.Format);
                    }
                    catch
                    {
                    }

                    progress.Report((++done, total));
                }
            },
            ct);
    }

    private static bool NeedsReverify(int bookId, string sourcePath)
    {
        string cachePath = BookCoverThumbnailPaths.GetCachePath(bookId);
        if (!File.Exists(cachePath))
        {
            return true;
        }

        try
        {
            return File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(cachePath);
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

    private static bool SourceReadable(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void RunCacheMaintenance(HashSet<int> validIds, int bookCount)
    {
        try
        {
            CollectOrphans(validIds);
            CoverCacheAttic.Prune(BookCoverThumbnailPaths.AtticDirectory);
            CoverCacheState.RecordBookCount(bookCount);
        }
        catch (Exception)
        {
        }
    }

    private static void CollectOrphans(HashSet<int> validIds)
    {
        foreach (string path in BookCoverThumbnailPaths.EnumerateAll().ToList())
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || !validIds.Contains(id))
            {
                CoverCacheAttic.MoveToAttic(path, BookCoverThumbnailPaths.AtticDirectory);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
