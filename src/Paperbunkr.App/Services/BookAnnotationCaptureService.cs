using System;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Crops a rectangular region out of a rendered PDF page and saves it as a standalone PNG
/// (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md §"PDF area
/// capture"). The design's own plan assumed this would extend <c>cYo.Common.Drawing.PdfImages</c>
/// (`Paperbunkr.Common/Drawing/PdfImages.cs`) - on inspection that's a legacy Ghostscript-shelling
/// utility unrelated to this app's actual PDF page rendering pipeline (which decodes straight to an
/// Avalonia <see cref="Bitmap"/> via <c>PageImageDecoder</c>), so this is a fresh, small, App-layer
/// service instead - same Skia crop/draw technique <see cref="BackdropBlurRenderer"/> already uses in
/// this codebase, not a new pattern.
/// </summary>
public static class BookAnnotationCaptureService
{
    /// <param name="sourcePage">The currently-rendered PDF page bitmap.</param>
    /// <param name="rectX">Crop region's left edge, as a fraction (0-1) of the page's own width.</param>
    /// <param name="rectY">Crop region's top edge, as a fraction (0-1) of the page's own height.</param>
    /// <param name="rectWidth">Crop region's width, as a fraction (0-1) of the page's own width.</param>
    /// <param name="rectHeight">Crop region's height, as a fraction (0-1) of the page's own height.</param>
    /// <param name="destinationDirectory">Created if it doesn't already exist.</param>
    /// <returns>Full path to the saved PNG.</returns>
    public static string CropAndSave(Bitmap sourcePage, double rectX, double rectY, double rectWidth, double rectHeight, string destinationDirectory)
    {
        var srcSize = sourcePage.PixelSize;
        int cropX = Math.Clamp((int)Math.Round(rectX * srcSize.Width), 0, srcSize.Width - 1);
        int cropY = Math.Clamp((int)Math.Round(rectY * srcSize.Height), 0, srcSize.Height - 1);
        int cropWidth = Math.Clamp((int)Math.Round(rectWidth * srcSize.Width), 1, srcSize.Width - cropX);
        int cropHeight = Math.Clamp((int)Math.Round(rectHeight * srcSize.Height), 1, srcSize.Height - cropY);

        using SKImage skImage = BackdropBlurRenderer.ToSkImage(sourcePage, srcSize);

        var info = new SKImageInfo(cropWidth, cropHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var srcRect = new SKRect(cropX, cropY, cropX + cropWidth, cropY + cropHeight);
        var destRect = new SKRect(0, 0, cropWidth, cropHeight);
        canvas.DrawImage(skImage, srcRect, destRect);
        canvas.Flush();

        Directory.CreateDirectory(destinationDirectory);
        string path = Path.Combine(destinationDirectory, $"{Guid.NewGuid():N}.png");

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);

        return path;
    }
}
