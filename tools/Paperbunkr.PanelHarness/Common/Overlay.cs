using Paperbunkr.App.Services.Reader.Panels;
using SkiaSharp;

namespace Paperbunkr.PanelHarness.Common;

/// <summary>A box to draw on an overlay: where (page fractions), in which colour, and the label drawn in its top-left corner.</summary>
internal readonly record struct OverlayBox(PanelRect Rect, SKColor Color, string? Label);

/// <summary>Draws panel boxes over a page and saves it as a PNG. Regular pages are scaled to fit 1100 px; tall strips are cut into side-by-side columns.</summary>
internal static class Overlay
{
    /// <summary>A detection result: green boxes when confident, red when not, numbered in reading order.</summary>
    public static void WriteResult(SKBitmap page, PagePanels result, string path)
    {
        var color = result.Confident ? SKColors.LimeGreen : SKColors.Red;
        Write(page, result.Rects.Select((r, i) => new OverlayBox(r, color, (i + 1).ToString())).ToList(), path);
    }

    public static void Write(SKBitmap page, IReadOnlyList<OverlayBox> boxes, string path)
    {
        if (page.Height > 3.0 * page.Width)
        {
            WriteTall(page, boxes, path);
            return;
        }

        double scale = Math.Min(1.0, 1100.0 / Math.Max(page.Width, page.Height));
        int w = Math.Max(1, (int)(page.Width * scale));
        int h = Math.Max(1, (int)(page.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(page, new SKRect(0, 0, w, h));
        DrawBoxes(canvas, boxes, w, h, strokeWidth: 4, textSize: 28);
        Save(surface, path);
    }

    // A webtoon strip is drawn at width 300 and cut into side-by-side columns of 1500 px so it can be read on one image.
    private static void WriteTall(SKBitmap page, IReadOnlyList<OverlayBox> boxes, string path)
    {
        const int colW = 300, colH = 1500, gap = 12;
        double scale = colW / (double)page.Width;
        int h = Math.Max(1, (int)(page.Height * scale));
        using var strip = SKSurface.Create(new SKImageInfo(colW, h));
        strip.Canvas.DrawBitmap(page, new SKRect(0, 0, colW, h));
        DrawBoxes(strip.Canvas, boxes, colW, h, strokeWidth: 3, textSize: 24);

        using var stripImage = strip.Snapshot();
        int cols = (h + colH - 1) / colH;
        using var surface = SKSurface.Create(new SKImageInfo((cols * (colW + gap)) - gap, Math.Min(colH, h)));
        surface.Canvas.Clear(SKColors.DimGray);
        for (int c = 0; c < cols; c++)
        {
            var src = new SKRect(0, c * colH, colW, Math.Min(h, (c + 1) * colH));
            surface.Canvas.DrawImage(stripImage, src, new SKRect(c * (colW + gap), 0, (c * (colW + gap)) + colW, src.Height));
        }

        Save(surface, path);
    }

    private static void DrawBoxes(SKCanvas canvas, IReadOnlyList<OverlayBox> boxes, int w, int h, float strokeWidth, float textSize)
    {
        using var font = new SKFont { Size = textSize };
        using var text = new SKPaint { Color = SKColors.Yellow, IsAntialias = true };
        using var shadow = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        foreach (var box in boxes)
        {
            using var stroke = new SKPaint { Color = box.Color, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth };
            var r = box.Rect;
            var rect = new SKRect((float)(r.X * w), (float)(r.Y * h), (float)(r.Right * w), (float)(r.Bottom * h));
            canvas.DrawRect(rect, stroke);
            if (box.Label is { Length: > 0 } label)
            {
                canvas.DrawText(label, rect.Left + 7, rect.Top + textSize + 3, font, shadow);
                canvas.DrawText(label, rect.Left + 6, rect.Top + textSize + 2, font, text);
            }
        }
    }

    private static void Save(SKSurface surface, string path)
    {
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 80);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
