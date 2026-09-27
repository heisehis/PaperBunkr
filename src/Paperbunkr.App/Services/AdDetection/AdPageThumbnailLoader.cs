using System;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services.AdDetection;

/// <summary>
/// Loads the small preview of an ad-library entry for Needs Review (docs/superpowers/specs/2026-09-21-comic-reader-
/// page-intelligence-design.md §5) by decoding the page that seeded it. Safe off the UI thread; returns null when the
/// source file or page is gone - the review row then shows a placeholder.
/// </summary>
public static class AdPageThumbnailLoader
{
    public const int ThumbnailHeight = 168;

    public static Bitmap? Load(string? filePath, int pageNumber)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        try
        {
            using var full = PageDecodeCore.DecodeSinglePage(filePath, pageNumber);
            if (full is null)
            {
                return null;
            }

            double scale = ThumbnailHeight / (double)full.PixelSize.Height;
            var size = new PixelSize(Math.Max(1, (int)Math.Round(full.PixelSize.Width * scale)), ThumbnailHeight);
            return full.CreateScaledBitmap(size);
        }
        catch
        {
            return null;
        }
    }
}
