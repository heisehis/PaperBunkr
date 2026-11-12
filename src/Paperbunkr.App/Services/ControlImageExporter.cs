using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Renders a control off screen to a PNG (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md §4). Extracted from the
/// Insights Year in Review poster export: the control is added to a host panel that is already in the live visual tree (so styles,
/// resources and the theme apply), measured and arranged at the requested size, rendered, saved, and always removed again.
/// </summary>
public static class ControlImageExporter
{
    /// <summary>Renders <paramref name="content"/> at <paramref name="size"/> (DIPs) times <paramref name="dpiScale"/> and saves it as PNG.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The pixel size is empty or larger than <paramref name="maxPixels"/> on a side.</exception>
    public static void RenderToPng(Panel host, Control content, Size size, string path, double dpiScale = 1, double maxPixels = 16000)
    {
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * dpiScale), (int)Math.Ceiling(size.Height * dpiScale));
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Width > maxPixels || pixels.Height > maxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(size), $"Can't render a {pixels.Width} × {pixels.Height} px image.");
        }

        host.Children.Add(content);
        try
        {
            // Measure and arrange explicitly at the export size. A full layout pass would arrange the content to the host's own size.
            content.Measure(size);
            content.Arrange(new Rect(size));
            using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * dpiScale, 96 * dpiScale));
            bitmap.Render(content);
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
        finally
        {
            host.Children.Remove(content);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }
}
