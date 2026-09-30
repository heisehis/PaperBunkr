using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>
/// Runs <see cref="PanelDetectionService"/> on a page the reader already holds (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 3): the bitmap is scaled down to
/// <see cref="OnnxPanelDetector.InputSize"/> on its long side (tall strips are analysed at a fixed narrow width) and detected. Detections are only cached in memory, never stored on disk.
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

            // Tall strips are analysed at a fixed narrow width; everything else is scaled to the model's input size.
            bool tall = size.Height > PanelDetectionService.TallAspect * size.Width;
            double scale = Math.Min(1.0, tall ? SkPanelAnalyzer.StripAnalysisWidth / (double)size.Width : OnnxPanelDetector.InputSize / (double)Math.Max(size.Width, size.Height));
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

            return PanelDetectionService.Detect(sk, rightToLeft);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException or IOException)
        {
            return PagePanels.Whole;
        }
    }

    /// <summary>Detects the panels of an already-decoded Skia page.</summary>
    public static PagePanels Analyze(SKBitmap page, bool rightToLeft) => PanelDetectionService.Detect(page, rightToLeft);
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
