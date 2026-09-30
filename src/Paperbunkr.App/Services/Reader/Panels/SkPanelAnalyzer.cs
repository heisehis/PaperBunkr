using System;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>
/// The Skia-only half of panel analysis: an already-decoded page in, panels out. Kept free of Avalonia types so the dev harness (tools/Paperbunkr.PanelHarness) can link it together with
/// <see cref="PanelDetector"/> and rebuild in seconds instead of building the whole app.
/// </summary>
public static class SkPanelAnalyzer
{
    /// <summary>The width a scrolling strip is analysed at (narrower strips are used as they are).</summary>
    public const int StripAnalysisWidth = 360;

    /// <summary>Detects the blocks of a tall scrolling strip (webtoon/manhwa) from its empty bands; see <see cref="PanelDetector.DetectStrip"/>.</summary>
    public static PagePanels AnalyzeStrip(SKBitmap strip)
    {
        SKBitmap? resized = null;
        try
        {
            var source = strip;
            if (strip.Width > StripAnalysisWidth)
            {
                double scale = StripAnalysisWidth / (double)strip.Width;
                resized = strip.Resize(new SKImageInfo(StripAnalysisWidth, Math.Max(16, (int)Math.Round(strip.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                source = resized ?? strip;
            }

            var luma = ToLuma(source);
            return PanelDetector.DetectStrip(luma, source.Width, source.Height);
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static byte[] ToLuma(SKBitmap source)
    {
        var luma = new byte[source.Width * source.Height];
        var pixels = source.Pixels;
        for (int i = 0; i < luma.Length; i++)
        {
            var c = pixels[i];
            // Transparent pixels count as white paper; the rest use Rec. 601 luma.
            double alpha = c.Alpha / 255.0;
            double value = ((0.299 * c.Red) + (0.587 * c.Green) + (0.114 * c.Blue)) * alpha + (255 * (1 - alpha));
            luma[i] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }

        return luma;
    }

    /// <summary>Detects the panels of a decoded Skia page (scaled first if larger than <see cref="PanelDetector.DetectionSize"/>).</summary>
    public static PagePanels Analyze(SKBitmap page, bool rightToLeft)
    {
        SKBitmap? resized = null;
        try
        {
            var source = page;
            int longSide = Math.Max(page.Width, page.Height);
            if (longSide > PanelDetector.DetectionSize)
            {
                double scale = PanelDetector.DetectionSize / (double)longSide;
                resized = page.Resize(new SKImageInfo(Math.Max(16, (int)Math.Round(page.Width * scale)), Math.Max(16, (int)Math.Round(page.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                source = resized ?? page;
            }

            int width = source.Width;
            int height = source.Height;
            return PanelDetector.Detect(ToLuma(source), width, height, rightToLeft);
        }
        finally
        {
            resized?.Dispose();
        }
    }
}
