using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using SvgSkia = Svg.Skia;

namespace Paperbunkr.App.Services;

/// <summary>
/// Rasterises a bundled brand / flag / rating SVG (<c>avares://.../Assets/Marks/**</c>) into a
/// cached <see cref="Bitmap"/> - the brand-iconography counterpart of
/// <see cref="BackdropBlurRenderer"/>, using the same SkiaSharp-surface → <see cref="WriteableBitmap"/>
/// pixel-copy technique.
///
/// Avalonia has no native SVG support and <c>Avalonia.Svg.Skia</c> targets Avalonia 11; this uses
/// <c>Svg.Skia</c> 5.1.0's core (pinned to the same <c>SkiaSharp</c> / <c>HarfBuzzSharp</c>
/// Avalonia 12.1 already ships). Marks are tiny and repeat heavily, so every (path, size, tint)
/// result is memoised for the process lifetime.
/// </summary>
public static class SvgMarkRenderer
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    // The contrast plate each rendered bitmap needs (see PublisherIconBitmaps.ClassifyPlate) - measured
    // once, from the pixels already in hand while rendering, and read back by BrandMark when a call
    // site asks for guaranteed contrast (publisher logos laid directly on cover art).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap, object> Plates = new();

    /// <summary>The plate a bitmap from <see cref="Render"/> needs; <see cref="IconPlate.None"/> for
    /// a bitmap this renderer did not produce.</summary>
    public static IconPlate PlateOf(Bitmap? bitmap) =>
        bitmap is not null && Plates.TryGetValue(bitmap, out object? plate) ? (IconPlate)plate : IconPlate.None;

    /// <summary>Rasterises <paramref name="avaresPath"/> into a bitmap fitted (aspect-preserving)
    /// so its <em>height</em> is <paramref name="targetHeight"/> px, with width following the
    /// SVG's own aspect - marks are always displayed by <c>Image.Height</c>, so a wide publisher
    /// wordmark comes back at full display resolution instead of a tiny longest-side fit that then
    /// gets upscaled. Callers pass a supersampled height (see <see cref="Controls.BrandMark"/>) so
    /// the downscale to display size stays crisp on high-DPI screens.</summary>
    /// <param name="tint">When set, every non-transparent pixel is replaced with this colour
    /// (SrcIn blend) - for single-colour marks that should follow the theme. Leave null to render
    /// the SVG's own colours.</param>
    public static Bitmap? Render(string avaresPath, int targetHeight, Color? tint = null)
    {
        if (string.IsNullOrWhiteSpace(avaresPath) || targetHeight <= 0)
        {
            return null;
        }

        string key = $"{avaresPath}|{targetHeight}|{(tint is { } c ? c.ToUInt32() : 0u)}";
        return Cache.GetOrAdd(key, _ => RenderUncached(avaresPath, targetHeight, tint));
    }

    /// <summary>Premultiplied BGRA bytes → unpremultiplied colours → <see cref="PublisherIconBitmaps.ClassifyPlate"/>.</summary>
    private static IconPlate ClassifyBgraPremul(byte[] bgra)
    {
        var pixels = new SKColor[bgra.Length / 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte b = bgra[i * 4], g = bgra[i * 4 + 1], r = bgra[i * 4 + 2], a = bgra[i * 4 + 3];
            if (a is > 0 and < 255)
            {
                r = (byte)Math.Min(255, r * 255 / a);
                g = (byte)Math.Min(255, g * 255 / a);
                b = (byte)Math.Min(255, b * 255 / a);
            }

            pixels[i] = new SKColor(r, g, b, a);
        }

        return PublisherIconBitmaps.ClassifyPlate(pixels);
    }

    private static Bitmap? RenderUncached(string avaresPath, int targetHeight, Color? tint)
    {
        try
        {
            var uri = new Uri(avaresPath);
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            // The SKPicture is owned by the SKSvg - it must stay alive until we finish drawing,
            // so everything happens inside this using block.
            using Stream stream = AssetLoader.Open(uri);
            using var svg = SvgSkia.SKSvg.CreateFromStream(stream);

            SKPicture? picture = svg.Picture;
            if (picture is null)
            {
                return null;
            }

            SKRect bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

            // Fit by height - marks render at Image.Height, so scaling by the SVG's own height
            // gives a bitmap that is exactly display-resolution (times the caller's supersample),
            // whether the art is square (flag), portrait (ESRB box) or a wide wordmark (publisher).
            float fit = targetHeight / bounds.Height;
            int w = Math.Max(1, (int)MathF.Round(bounds.Width * fit));
            int h = Math.Max(1, (int)MathF.Round(bounds.Height * fit));
            var target = new PixelSize(w, h);

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(fit);
            canvas.Translate(-bounds.Left, -bounds.Top);

            if (tint is { } t)
            {
                using var paint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(t.R, t.G, t.B, t.A), SKBlendMode.SrcIn),
                };
                canvas.DrawPicture(picture, paint);
            }
            else
            {
                canvas.DrawPicture(picture);
            }

            canvas.Flush();

            using SKImage snapshot = surface.Snapshot();
            using SKPixmap pixmap = snapshot.PeekPixels();
            byte[] pixelBytes = pixmap.GetPixelSpan().ToArray();

            var writeable = new WriteableBitmap(target, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (ILockedFramebuffer fb = writeable.Lock())
            {
                Marshal.Copy(pixelBytes, 0, fb.Address, pixelBytes.Length);
            }

            Plates.Add(writeable, ClassifyBgraPremul(pixelBytes));
            return writeable;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
