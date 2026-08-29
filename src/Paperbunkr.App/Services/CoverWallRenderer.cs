using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Composes a "living cover-wall" for the Home masthead (docs/superpowers/specs/
/// 2026-08-28-home-screen-redesign-design.md §2): a handful of the user's own cover thumbnails
/// tiled across a coarse grid, then blurred and heavily darkened so it reads as atmosphere rather
/// than a legible collage. Same SkiaSharp-direct approach as <see cref="BackdropBlurRenderer"/>
/// (bypassing Avalonia's Effect pipeline, AvaloniaUI/Avalonia#11416), and reuses its
/// <see cref="BackdropBlurRenderer.ToSkImage"/> pixel-copy helper.
///
/// Returns <see langword="null"/> when there are no covers to work with; the caller falls back to a
/// flat gradient masthead.
/// </summary>
public static class CoverWallRenderer
{
    public static Bitmap? Render(IReadOnlyList<Bitmap> covers, PixelSize targetSize, float blurSigma = 42f)
    {
        if (covers is null || covers.Count == 0 || targetSize.Width <= 0 || targetSize.Height <= 0)
        {
            return null;
        }

        var info = new SKImageInfo(targetSize.Width, targetSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Pass 1 - tile the covers sharp across a grid.
        using var tileSurface = SKSurface.Create(info);
        var tile = tileSurface.Canvas;
        tile.Clear(new SKColor(8, 8, 10));

        int count = covers.Count;
        int cols = count <= 3 ? Math.Max(1, count) : (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)cols);
        float cellW = (float)targetSize.Width / cols;
        float cellH = (float)targetSize.Height / rows;

        for (int i = 0; i < count; i++)
        {
            var src = covers[i];
            var srcSize = src.PixelSize;
            if (srcSize.Width <= 0 || srcSize.Height <= 0)
            {
                continue;
            }

            using SKImage skImage = BackdropBlurRenderer.ToSkImage(src, srcSize);

            int cx = i % cols;
            int cy = i / cols;
            // slight bleed so grid seams don't survive the blur
            var cell = new SKRect(cx * cellW - 4f, cy * cellH - 4f, (cx + 1) * cellW + 4f, (cy + 1) * cellH + 4f);

            float scale = Math.Max(cell.Width / srcSize.Width, cell.Height / srcSize.Height);
            float drawW = srcSize.Width * scale;
            float drawH = srcSize.Height * scale;
            var dest = new SKRect(
                cell.MidX - drawW / 2f,
                cell.MidY - drawH / 2f,
                cell.MidX + drawW / 2f,
                cell.MidY + drawH / 2f);

            tile.Save();
            tile.ClipRect(cell);
            tile.DrawImage(skImage, dest);
            tile.Restore();
        }

        tile.Flush();
        using var tiled = tileSurface.Snapshot();

        // Pass 2 - blur the whole thing, then lay a heavy near-black scrim + a radial vignette.
        using var outSurface = SKSurface.Create(info);
        var canvas = outSurface.Canvas;
        canvas.Clear(SKColors.Black);

        var full = new SKRect(0, 0, targetSize.Width, targetSize.Height);
        using (var blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma, SKShaderTileMode.Clamp) })
        {
            canvas.DrawImage(tiled, full, blurPaint);
        }

        using (var scrim = new SKPaint { Color = new SKColor(8, 8, 10, 205) })
        {
            canvas.DrawRect(full, scrim);
        }

        using (var vignette = new SKPaint
               {
                   Shader = SKShader.CreateRadialGradient(
                       new SKPoint(targetSize.Width / 2f, targetSize.Height * 0.35f),
                       Math.Max(targetSize.Width, targetSize.Height) * 0.75f,
                       new[] { new SKColor(0, 0, 0, 0), new SKColor(6, 6, 6, 235) },
                       new[] { 0.35f, 1f },
                       SKShaderTileMode.Clamp),
               })
        {
            canvas.DrawRect(full, vignette);
        }

        canvas.Flush();

        using var snapshot = outSurface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        byte[] pixelBytes = pixmap.GetPixelSpan().ToArray();

        var writeable = new WriteableBitmap(targetSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = writeable.Lock())
        {
            Marshal.Copy(pixelBytes, 0, fb.Address, pixelBytes.Length);
        }

        return writeable;
    }
}
