using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>
/// Runs <see cref="PanelDetector"/> on a page the reader already holds (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 3): the bitmap is scaled down to
/// <see cref="PanelDetector.AnalysisSize"/> on its long side, turned into luminance and detected. Cheap enough (a few milliseconds) that nothing is stored on disk.
/// </summary>
public static class PagePanelAnalyzer
{
    /// <summary>Detects the panels of <paramref name="page"/>. Returns <see cref="PagePanels.Whole"/> if the bitmap cannot be read (for instance because the pipeline disposed it meanwhile).</summary>
    public static PagePanels Analyze(Bitmap page, bool rightToLeft)
    {
        try
        {
            var size = page.PixelSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return PagePanels.Whole;
            }

            double scale = Math.Min(1.0, PanelDetector.AnalysisSize / (double)Math.Max(size.Width, size.Height));
            var target = new PixelSize(Math.Max(16, (int)Math.Round(size.Width * scale)), Math.Max(16, (int)Math.Round(size.Height * scale)));

            using var scaled = page.CreateScaledBitmap(target, BitmapInterpolationMode.MediumQuality);
            using var stream = new MemoryStream();
            scaled.Save(stream);
            stream.Position = 0;
            using var sk = SKBitmap.Decode(stream);
            if (sk is null)
            {
                return PagePanels.Whole;
            }

            return Analyze(sk, rightToLeft);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException or IOException)
        {
            return PagePanels.Whole;
        }
    }

    /// <summary>Detects the panels of an already-decoded Skia page (scaled first if it is larger than the analysis size).</summary>
    public static PagePanels Analyze(SKBitmap page, bool rightToLeft)
    {
        SKBitmap? resized = null;
        try
        {
            var source = page;
            int longSide = Math.Max(page.Width, page.Height);
            if (longSide > PanelDetector.AnalysisSize)
            {
                double scale = PanelDetector.AnalysisSize / (double)longSide;
                resized = page.Resize(new SKImageInfo(Math.Max(16, (int)Math.Round(page.Width * scale)), Math.Max(16, (int)Math.Round(page.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                source = resized ?? page;
            }

            int width = source.Width;
            int height = source.Height;
            var luma = new byte[width * height];
            var pixels = source.Pixels;
            for (int i = 0; i < luma.Length; i++)
            {
                var c = pixels[i];
                // Transparent pixels count as white paper; the rest use Rec. 601 luma.
                double alpha = c.Alpha / 255.0;
                double value = ((0.299 * c.Red) + (0.587 * c.Green) + (0.114 * c.Blue)) * alpha + (255 * (1 - alpha));
                luma[i] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
            }

            return PanelDetector.Detect(luma, width, height, rightToLeft);
        }
        finally
        {
            resized?.Dispose();
        }
    }
}

/// <summary>
/// A small in-memory cache of detected panels, keyed by issue, page and reading direction (the order depends on direction), so stepping back and forth or double-clicking twice does not analyse a page
/// again. The last <see cref="Capacity"/> pages are kept. Thread-safe.
/// </summary>
public sealed class PagePanelCache
{
    public const int Capacity = 8;

    private readonly object _lock = new();
    private readonly LinkedList<((int Issue, int Page, bool Rtl) Key, PagePanels Panels)> _order = new();
    private readonly Dictionary<(int Issue, int Page, bool Rtl), LinkedListNode<((int Issue, int Page, bool Rtl) Key, PagePanels Panels)>> _map = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(int issueId, int pageIndex, bool rightToLeft, out PagePanels panels)
    {
        lock (_lock)
        {
            if (_map.TryGetValue((issueId, pageIndex, rightToLeft), out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                panels = node.Value.Panels;
                return true;
            }
        }

        panels = PagePanels.Whole;
        return false;
    }

    public void Set(int issueId, int pageIndex, bool rightToLeft, PagePanels panels)
    {
        var key = (issueId, pageIndex, rightToLeft);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = _order.AddFirst((key, panels));
            _map[key] = node;
            while (_map.Count > Capacity)
            {
                var last = _order.Last!;
                _map.Remove(last.Value.Key);
                _order.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _order.Clear();
            _map.Clear();
        }
    }
}
