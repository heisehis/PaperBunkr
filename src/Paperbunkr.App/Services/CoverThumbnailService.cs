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
/// §1-2), decoding via the already-proven <see cref="PageImageDecoder"/> Reader Canvas pipeline
/// rather than the ported ComicRack CE thumbnail-cache machinery
/// (<c>Paperbunkr.Engine/IO/Cache</c>), which is built on <c>System.Drawing.Bitmap</c>. Avalonia's
/// own <see cref="Bitmap"/> supports scaling and JPEG encoding natively (confirmed against the
/// installed Avalonia version), so no GDI round-trip is needed here.
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
    /// Decodes <paramref name="filePath"/>'s first page and saves a scaled-down JPEG to
    /// <see cref="CoverThumbnailPaths.GetCachePath"/>. Returns false (without throwing) for a
    /// missing/unsupported/corrupt file - callers treat that as "skip, try again next run".
    /// Presence-checked internally too, so this is safe to call directly in tests without going
    /// through <see cref="GenerateAllAsync"/>.
    /// </summary>
    public bool TryGenerateThumbnail(int issueId, string filePath)
    {
        bool ok = TryGenerateThumbnail(issueId, filePath, out double? aspectRatio);
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
    internal bool TryGenerateThumbnail(int issueId, string filePath, out double? aspectRatio)
    {
        aspectRatio = null;
        string destPath = CoverThumbnailPaths.GetCachePath(issueId);
        if (File.Exists(destPath))
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
    /// <c>SetCustomBookThumbnail</c> (MainForm.cs) refuses linked books entirely, confirmed by
    /// checking CE source per this project's standing rule (docs/superpowers/specs/2026-08-23-
    /// cover-art-override-design.md). Always overwrites, unlike <see cref="TryGenerateThumbnail"/>'s
    /// presence-check - that's what makes this an override rather than a fill-the-gap generation.
    /// Uses <see cref="CoverImageCache.InvalidateMemoryOnly"/>, not <see cref="CoverImageCache.Invalidate"/> -
    /// the latter would delete the very file this method just wrote.
    /// </summary>
    public bool TrySetCustomCover(int issueId, string sourceImagePath)
    {
        string destPath = CoverThumbnailPaths.GetCachePath(issueId);
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
            CoverImageCache.InvalidateMemoryOnly(issueId);
            CoverAspectRatioStore.ReportRatio(issueId, size.Width / (double)size.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reverts a custom cover: deletes the cached file (real <see cref="CoverImageCache.Invalidate"/>
    /// this time - the file itself must go) and, if the issue actually has a linked file, regenerates
    /// the real decoded-page-1 cover immediately rather than leaving the cover blank until something
    /// else happens to call <see cref="TryGenerateThumbnail"/> again.
    /// </summary>
    public void ResetCover(int issueId, string? filePath)
    {
        CoverImageCache.Invalidate(issueId);
        if (!string.IsNullOrEmpty(filePath))
        {
            TryGenerateThumbnail(issueId, filePath);
        }
    }

    /// <summary>
    /// Generates thumbnails for every Issue that has a file path but no cached thumbnail yet.
    /// Presence-based - re-running after adding new comics only fills the gaps. One bad file
    /// doesn't stop the batch.
    /// </summary>
    public async Task GenerateAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        await Task.Run(
            () =>
            {
                using var context = _contextFactory();
                var candidates = context.Issues
                    .Where(i => i.FilePath != null)
                    .Select(i => new { i.Id, i.FilePath })
                    .ToList()
                    .Where(i => !File.Exists(CoverThumbnailPaths.GetCachePath(i.Id)))
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
                        if (TryGenerateThumbnail(candidate.Id, candidate.FilePath!, out double? ratio) && ratio is double r)
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
        var pendingIds = context.Issues
            .Where(i => i.CoverAspectRatio == null)
            .Select(i => i.Id)
            .ToList();

        var learned = new List<(int IssueId, double Ratio)>();
        foreach (int id in pendingIds)
        {
            ct.ThrowIfCancellationRequested();
            string path = CoverThumbnailPaths.GetCachePath(id);
            if (File.Exists(path) && CoverImageDimensions.TryRead(path, out int w, out int h) && w > 0 && h > 0)
            {
                learned.Add((id, w / (double)h));
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
}
