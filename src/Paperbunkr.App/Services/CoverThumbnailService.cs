using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generates real cover thumbnails from comic archives (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md
/// §1-2), decoding via the already-proven <see cref="PageImageDecoder"/> Reader Canvas pipeline.
/// Avalonia's own <see cref="Bitmap"/> supports scaling and JPEG encoding natively, so no GDI
/// round-trip is needed here.
///
/// <para>
/// Cache files are named <c>{issueId}-{fingerprint}.jpg</c> (docs/superpowers/specs/2026-08-27-
/// cover-thumbnail-identity-validation-design.md) where the fingerprint folds in the issue's
/// current file path + size (<see cref="CoverFingerprint"/>). A cached file is only trusted when
/// both parts match, so a library rebuild that reassigns <c>Issue.Id</c> values can no longer
/// serve the previous issue's cover for a reused id.
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
    /// Decodes <paramref name="filePath"/>'s first page and saves a scaled-down JPEG to this
    /// issue's fingerprinted cache path. Any stale <c>{issueId}-*.jpg</c> sibling (a cover for a
    /// different file that used to hold this id) is deleted first. Returns false (without throwing)
    /// for a missing/unsupported/corrupt file - callers treat that as "skip, try again next run".
    /// <paramref name="fileSize"/> is the issue's persisted <c>FileSize</c>; pass it so the stem
    /// here matches the one <see cref="GenerateAllAsync"/> and the card models compute.
    /// <paramref name="force"/> skips the presence check - the identity fingerprint alone doesn't
    /// catch a cache file that's wrong from the moment it was written (docs/superpowers/specs/
    /// 2026-08-30-cover-thumbnail-content-verification-design.md), so <see cref="VerifyAllAsync"/>
    /// passes true to unconditionally re-derive every cover from source.
    /// </summary>
    public bool TryGenerateThumbnail(int issueId, string filePath, long? fileSize = null, bool force = false)
    {
        bool ok = TryGenerateThumbnail(issueId, filePath, fileSize, force, out double? aspectRatio);
        if (ok && aspectRatio is double ratio)
        {
            // In-memory only here - a single generation (ResetCover, the plugin API, a first scan
            // of one file) reports to the store, whose debounced write-back persists it. The bulk
            // paths (GenerateAllAsync / BackfillAspectRatios) batch straight to the DB instead.
            CoverAspectRatioStore.ReportRatio(issueId, ratio);
        }

        return ok;
    }

    /// <summary>
    /// Generation core. <paramref name="aspectRatio"/> is the source cover's width/height when a
    /// thumbnail was produced (or was already present and can be re-measured from its JPEG header),
    /// else null. Callers decide how to persist it.
    /// </summary>
    internal bool TryGenerateThumbnail(int issueId, string filePath, long? fileSize, bool force, out double? aspectRatio)
    {
        aspectRatio = null;
        string stem = CoverFingerprint.Stem(issueId, filePath, fileSize);
        string destPath = CoverThumbnailPaths.GetCachePath(stem);
        if (!force && File.Exists(destPath))
        {
            if (CoverImageDimensions.TryRead(destPath, out int w, out int h))
            {
                aspectRatio = w / (double)h;
            }

            return true;
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
            SweepStaleSiblings(issueId, keepStem: stem); // only after the new file is safely written
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Overrides an issue's displayed cover with a user-picked local image, regardless of whether
    /// the issue has a real linked file - a deliberate deviation from ComicRackCE, whose own
    /// <c>SetCustomBookThumbnail</c> refuses linked books entirely (docs/superpowers/specs/
    /// 2026-08-23-cover-art-override-design.md). Always overwrites, unlike
    /// <see cref="TryGenerateThumbnail"/>'s presence-check. Writes to the issue's <b>current</b>
    /// fingerprinted stem (resolved from the database) and sweeps any stale sibling, so the
    /// override survives an id/path check the same way a generated cover does.
    /// </summary>
    public bool TrySetCustomCover(int issueId, string sourceImagePath)
    {
        string stem = StemFor(issueId);
        string destPath = CoverThumbnailPaths.GetCachePath(stem);
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
            SweepStaleSiblings(issueId, keepStem: stem); // only after the new file is safely written
            CoverImageCache.InvalidateMemoryOnly(stem);
            CoverAspectRatioStore.ReportRatio(issueId, size.Width / (double)size.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reverts a custom cover: deletes every cached file for this id (<see cref="CoverImageCache.Invalidate"/>)
    /// and, if the issue actually has a linked file, regenerates the real decoded-page-1 cover
    /// immediately rather than leaving it blank until something else calls
    /// <see cref="TryGenerateThumbnail"/> again.
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
    /// Generates thumbnails for every Issue that has a file path but no cached thumbnail for its
    /// current fingerprint, then deletes orphaned cache files whose stem no longer matches any
    /// current issue (covers deleted issues/series and pre-rework <c>{id}.jpg</c> files).
    /// Presence-based - re-running after a rebuild that re-imported the same files at the same
    /// paths regenerates nothing. One bad file doesn't stop the batch.
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

                var validStems = new HashSet<string>(
                    all.Select(i => CoverFingerprint.Stem(i.Id, i.FilePath, i.FileSize)),
                    StringComparer.Ordinal);

                var candidates = all
                    .Where(i => !File.Exists(CoverThumbnailPaths.GetCachePath(
                        CoverFingerprint.Stem(i.Id, i.FilePath, i.FileSize))))
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

                // Whether or not any new thumbnail was generated above, sweep every issue whose
                // cover exists on disk but has no stored aspect ratio yet - the upgrade case, where
                // covers were all generated before Issue.CoverAspectRatio existed. Panorama needs
                // this to render real cover shapes without re-decoding every cover on screen.
                BackfillAspectRatiosCore(ct);
                CollectOrphans(validStems);
            },
            ct);
    }

    /// <summary>
    /// Fills <c>Issue.CoverAspectRatio</c> for every issue that has a cached cover file but no
    /// stored ratio, by reading each JPEG's dimensions from its header (no full decode). Safe to
    /// call directly; <see cref="GenerateAllAsync"/> already runs it. One unreadable file doesn't
    /// stop the sweep.
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
            string stem = CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize);
            string path = CoverThumbnailPaths.GetCachePath(stem);
            if (File.Exists(path) && CoverImageDimensions.TryRead(path, out int w, out int h) && w > 0 && h > 0)
            {
                learned.Add((issue.Id, w / (double)h));
            }
        }

        PersistAspectRatios(learned);
    }

    /// <summary>
    /// Writes learned <c>(issueId, ratio)</c> pairs to <c>Issue.CoverAspectRatio</c> in one batch
    /// and primes <see cref="CoverAspectRatioStore"/> so a running session picks them up without a
    /// reload. Best-effort - a write failure just means the value is re-learned later.
    /// </summary>
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
    /// Unconditionally re-derives every linked Issue's cover from its source file and overwrites
    /// the cache, then runs the same orphan GC as <see cref="GenerateAllAsync"/> (docs/superpowers/
    /// specs/2026-08-30-cover-thumbnail-content-verification-design.md). Every candidate gets a
    /// real decode+scale+encode+write, not just the ones missing a fingerprint-matching file - the
    /// identity fingerprint alone doesn't catch a cache entry that was wrong from the moment it was
    /// written. Heavier than <see cref="GenerateAllAsync"/>; only the manual "Verify & Repair
    /// Covers" action and the periodic background sweep call this, never the startup/library-load
    /// reconcile.
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

                var validStems = new HashSet<string>(
                    all.Select(i => CoverFingerprint.Stem(i.Id, i.FilePath, i.FileSize)),
                    StringComparer.Ordinal);

                int total = all.Count;
                int done = 0;
                progress.Report((0, total));

                foreach (var candidate in all)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        TryGenerateThumbnail(candidate.Id, candidate.FilePath!, candidate.FileSize, force: true);
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

    /// <summary>Deletes every <c>{issueId}-*.jpg</c> except <paramref name="keepStem"/>'s file.</summary>
    private static void SweepStaleSiblings(int issueId, string keepStem)
    {
        string keepName = keepStem + ".jpg";
        foreach (string path in CoverThumbnailPaths.EnumerateForIssue(issueId).ToList())
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

    /// <summary>Deletes cache files whose stem isn't in <paramref name="validStems"/>.</summary>
    private static void CollectOrphans(HashSet<string> validStems)
    {
        foreach (string path in CoverThumbnailPaths.EnumerateAll().ToList())
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

    private string StemFor(int issueId)
    {
        using var context = _contextFactory();
        var issue = context.Issues
            .Where(i => i.Id == issueId)
            .Select(i => new { i.FilePath, i.FileSize })
            .FirstOrDefault();
        return CoverFingerprint.Stem(issueId, issue?.FilePath, issue?.FileSize);
    }

    private long? FileSizeFor(int issueId)
    {
        using var context = _contextFactory();
        return context.Issues.Where(i => i.Id == issueId).Select(i => i.FileSize).FirstOrDefault();
    }
}
