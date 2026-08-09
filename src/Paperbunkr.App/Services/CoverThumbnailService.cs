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
        string destPath = CoverThumbnailPaths.GetCachePath(issueId);
        if (File.Exists(destPath))
        {
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
            return true;
        }
        catch
        {
            return false;
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

                foreach (var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        TryGenerateThumbnail(candidate.Id, candidate.FilePath!);
                    }
                    catch
                    {
                        // One bad file doesn't stop the batch.
                    }

                    progress.Report((++done, total));
                }
            },
            ct);
    }
}
