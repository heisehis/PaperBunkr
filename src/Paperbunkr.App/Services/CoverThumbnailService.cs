using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generates real cover thumbnails from comic archives (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md
/// §1-2), decoding via the already-proven <see cref="PageImageDecoder"/> Reader Canvas pipeline.
///
/// <para>
/// Cache files are named <c>{issueId}.jpg</c> (docs/superpowers/specs/2026-09-06-scheduled-tasks-
/// and-cover-durability-design.md). A cover is only ever <b>moved to the attic</b>
/// (<see cref="CoverCacheAttic"/>) when its id matches no issue row at all - a routine file-path
/// change (metadata write-back, a move, the <c>~RF*.TMP</c> watch bug) no longer touches it,
/// because the file name doesn't encode the path any more. id-reuse after a library rebuild is
/// handled by <see cref="CoverCacheState"/>'s explicit purge, not by a per-file fingerprint.
/// User-picked covers live in <see cref="CustomCoverPaths"/> and are never swept.
/// </para>
/// </summary>
public class CoverThumbnailService
{
    private const int ThumbnailLongestEdge = 400;
    private const int JpegQuality = 85;

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public CoverThumbnailService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal CoverThumbnailService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Decodes <paramref name="filePath"/>'s first page and saves a scaled-down JPEG to this issue's
    /// cache path (<c>{issueId}.jpg</c>). Returns false (without throwing) for a
    /// missing/unsupported/corrupt file - callers treat that as "skip, try again next run".
    /// <paramref name="force"/> skips the presence check so <see cref="VerifyAllAsync"/> can
    /// unconditionally re-derive a cover from source.
    /// </summary>
    public bool TryGenerateThumbnail(int issueId, string filePath, long? fileSize = null, bool force = false)
    {
        bool ok = TryGenerateThumbnail(issueId, filePath, fileSize, force, out double? aspectRatio);
        if (ok && aspectRatio is double ratio)
        {
            CoverAspectRatioStore.ReportRatio(issueId, ratio);
        }

        return ok;
    }

    /// <summary>
    /// Generation core. <paramref name="aspectRatio"/> is the source cover's width/height when a
    /// thumbnail was produced (or was already present and can be re-measured from its JPEG header),
    /// else null.
    /// </summary>
    internal bool TryGenerateThumbnail(int issueId, string filePath, long? fileSize, bool force, out double? aspectRatio)
    {
        aspectRatio = null;
        string destPath = CoverThumbnailPaths.GetCachePath(issueId);

        if (!force)
        {
            if (!File.Exists(destPath))
            {
                // A cover atticked while this issue was briefly gone (a DB restore, a healed path)
                // is cheaper to move back than to re-decode.
                CoverCacheAttic.TryRestoreById(issueId, CoverThumbnailPaths.ThumbnailDirectory, CoverThumbnailPaths.AtticDirectory);
            }

            if (File.Exists(destPath))
            {
                if (CoverImageDimensions.TryRead(destPath, out int w, out int h))
                {
                    aspectRatio = w / (double)h;
                }

                return true;
            }
        }

        using var decoder = PageImageDecoder.TryOpen(filePath);
        if (decoder is null)
        {
            return false;
        }

        try
        {
            Bitmap page = decoder.GetPage(0); // owned by the decoder's own cache - do not dispose
            var size = page.PixelSize;
            int longest = Math.Max(size.Width, size.Height);
            if (longest <= 0)
            {
                return false;
            }

            double scale = Math.Min(1.0, (double)ThumbnailLongestEdge / longest);
            var target = new PixelSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));

            using Bitmap scaled = page.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            scaled.Save(destPath, new JpegBitmapEncoderOptions { Quality = JpegQuality });
            aspectRatio = size.Width / (double)size.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Overrides an issue's displayed cover with a user-picked local image, written to
    /// <see cref="CustomCoverPaths"/> - its own directory, never swept by the orphan GC or the
    /// rebuild purge. Any generated <c>{id}.jpg</c> is removed so the custom art wins cleanly.
    /// </summary>
    public bool TrySetCustomCover(int issueId, string sourceImagePath)
    {
        string destPath = CustomCoverPaths.GetCachePath(issueId);
        try
        {
            using var source = new Bitmap(sourceImagePath);
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

            TryDelete(CoverThumbnailPaths.GetCachePath(issueId));
            CoverImageCache.InvalidateMemoryOnly(issueId.ToString(CultureInfo.InvariantCulture));
            CoverAspectRatioStore.ReportRatio(issueId, size.Width / (double)size.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reverts a custom cover: deletes the custom + generated file and the in-memory entry, then
    /// regenerates the real decoded-page-1 cover if the issue has a linked file.
    /// </summary>
    public void ResetCover(int issueId, string? filePath)
    {
        CoverImageCache.Invalidate(issueId);
        if (!string.IsNullOrEmpty(filePath))
        {
            TryGenerateThumbnail(issueId, filePath, FileSizeFor(issueId));
        }
    }

    /// <summary>
    /// Generates thumbnails for every Issue that has a file path but no cached thumbnail, then
    /// attics cache files whose id matches no current issue. Presence-based, one bad file doesn't
    /// stop the batch.
    /// </summary>
    public async Task GenerateAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var all = context.Issues
                    .Where(i => i.FilePath != null)
                    .Select(i => new { i.Id, i.FilePath, i.FileSize })
                    .ToList();

                var validIds = new HashSet<int>(all.Select(i => i.Id));

                var candidates = all
                    .Where(i => !File.Exists(CoverThumbnailPaths.GetCachePath(i.Id))
                                && !CustomCoverPaths.Exists(i.Id))
                    .ToList();

                int total = candidates.Count;
                int done = 0;
                progress.Report((0, total));

                var learned = new List<(int IssueId, double Ratio)>();
                foreach (var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (TryGenerateThumbnail(candidate.Id, candidate.FilePath!, candidate.FileSize, force: false, out double? ratio) && ratio is double r)
                        {
                            learned.Add((candidate.Id, r));
                        }
                    }
                    catch
                    {
                        // One bad file doesn't stop the batch.
                    }

                    progress.Report((++done, total));
                }

                PersistAspectRatios(learned);
                BackfillAspectRatiosCore(ct);
                RunCacheMaintenance(validIds, all.Count);
            },
            ct);
    }

    /// <summary>
    /// Orphan attic + prune + count record - best-effort housekeeping that must never fail the
    /// generation it rides on (a caller like <c>LiveFolderWatchService</c> chains a user-visible
    /// toast right after the await).
    /// </summary>
    private static void RunCacheMaintenance(HashSet<int> validIds, int issueCount)
    {
        try
        {
            CollectOrphans(validIds);
            CoverCacheAttic.Prune(CoverThumbnailPaths.AtticDirectory);
            CoverCacheState.RecordIssueCount(issueCount);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Fills <c>Issue.CoverAspectRatio</c> for every issue that has a cached cover file but no
    /// stored ratio, by reading each JPEG's dimensions from its header (no full decode).
    /// </summary>
    public Task BackfillAspectRatios(CancellationToken ct = default) =>
        Task.Run(() => BackfillAspectRatiosCore(ct), ct);

    private void BackfillAspectRatiosCore(CancellationToken ct)
    {
        using var context = _contextFactory();
        var pending = context.Issues
            .Where(i => i.CoverAspectRatio == null)
            .Select(i => new { i.Id, i.FilePath, i.FileSize })
            .ToList();

        var learned = new List<(int IssueId, double Ratio)>();
        foreach (var issue in pending)
        {
            ct.ThrowIfCancellationRequested();
            string path = CoverThumbnailPaths.GetCachePath(issue.Id);
            if (!File.Exists(path))
            {
                path = CustomCoverPaths.GetCachePath(issue.Id);
            }

            if (File.Exists(path) && CoverImageDimensions.TryRead(path, out int w, out int h) && w > 0 && h > 0)
            {
                learned.Add((issue.Id, w / (double)h));
            }
        }

        PersistAspectRatios(learned);
    }

    private void PersistAspectRatios(IReadOnlyCollection<(int IssueId, double Ratio)> learned)
    {
        if (learned.Count == 0)
        {
            return;
        }

        try
        {
            var byId = learned
                .Where(x => x.Ratio > 0 && !double.IsNaN(x.Ratio) && !double.IsInfinity(x.Ratio))
                .GroupBy(x => x.IssueId)
                .ToDictionary(g => g.Key, g => g.Last().Ratio);

            using var context = _contextFactory();
            var ids = byId.Keys.ToList();
            foreach (var row in context.Issues.Where(i => ids.Contains(i.Id)))
            {
                row.CoverAspectRatio = byId[row.Id];
            }

            context.SaveChanges();
            CoverAspectRatioStore.Prime(byId.Select(kv => (kv.Key, kv.Value)));
        }
        catch (Exception)
        {
            // Best-effort persistence - cover generation itself already succeeded.
        }
    }

    /// <summary>
    /// Re-derives an Issue's cover from source only when the source file changed since the cover was
    /// made (mtime-smart, docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-
    /// design.md) - replaces the old force-decode-everything pass. Then runs the same id-based
    /// orphan attic + prune as <see cref="GenerateAllAsync"/>.
    /// </summary>
    public async Task VerifyAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var all = context.Issues
                    .Where(i => i.FilePath != null)
                    .Select(i => new { i.Id, i.FilePath, i.FileSize })
                    .ToList();

                var validIds = new HashSet<int>(all.Select(i => i.Id));

                int total = all.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var candidate in all)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (NeedsReverify(candidate.Id, candidate.FilePath!))
                        {
                            TryGenerateThumbnail(candidate.Id, candidate.FilePath!, candidate.FileSize, force: true);
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

    /// <summary>
    /// Regenerates covers only for issues that currently have <b>no</b> cover (generated or custom)
    /// and whose source file is readable right now. Watchable + cancellable + resumable
    /// (presence-based). Never attics anything.
    /// </summary>
    public async Task RepairMissingAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var missing = context.Issues
                    .Where(i => i.FilePath != null)
                    .Select(i => new { i.Id, i.FilePath, i.FileSize })
                    .ToList()
                    .Where(i => !File.Exists(CoverThumbnailPaths.GetCachePath(i.Id))
                                && !CustomCoverPaths.Exists(i.Id)
                                && SourceReadable(i.FilePath!))
                    .ToList();

                int total = missing.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var issue in missing)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        TryGenerateThumbnail(issue.Id, issue.FilePath!, issue.FileSize, force: false);
                    }
                    catch
                    {
                    }

                    progress.Report((++done, total));
                }
            },
            ct);
    }

    private static bool NeedsReverify(int issueId, string sourcePath)
    {
        string cachePath = CoverThumbnailPaths.GetCachePath(issueId);
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
            return false; // source unreadable right now - leave the existing cover alone
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

    /// <summary>Attics cache files whose id matches no live issue - the only sweep that ever runs.</summary>
    private static void CollectOrphans(HashSet<int> validIds)
    {
        foreach (string path in CoverThumbnailPaths.EnumerateAll().ToList())
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || !validIds.Contains(id))
            {
                CoverCacheAttic.MoveToAttic(path, CoverThumbnailPaths.AtticDirectory);
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

    private long? FileSizeFor(int issueId)
    {
        using var context = _contextFactory();
        return context.Issues.Where(i => i.Id == issueId).Select(i => i.FileSize).FirstOrDefault();
    }
}
