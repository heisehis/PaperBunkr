using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>
/// Panel detection with a YOLO26-nano model exported to ONNX (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md step 3): the page is letterboxed into a
/// 1024x1024 square, the network returns up to 300 end-to-end detections (x1, y1, x2, y2, score, class in letterbox pixels; class 0 = panel, 1 = text, no NMS needed), and the panel boxes
/// become fractions of the page in reading order. Avalonia-free so the dev harness can link it. Instances hold an inference session and are thread-safe (a session runs concurrently).
/// </summary>
public sealed class OnnxPanelDetector : IDisposable
{
    public const int InputSize = 1024;

    /// <summary>Detections below this score are ignored.</summary>
    public const float MinScore = 0.25f;

    /// <summary>Boxes smaller than this fraction of the page in either direction are noise.</summary>
    public const double MinSide = 0.03;

    private const byte PadValue = 114;
    private const int PanelClass = 0;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public OnnxPanelDetector(string modelPath)
    {
        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
    }

    /// <summary>Detects panels on a decoded page. Returns <see cref="PagePanels.Whole"/> (unconfident) when nothing usable is found.</summary>
    public PagePanels Detect(SKBitmap page, bool rightToLeft)
    {
        var boxes = Run(page);
        if (boxes.Count > 0)
        {
            boxes.AddRange(FindBorderlessBands(page, boxes));
        }

        return ToPanels(boxes, rightToLeft);
    }

    /// <summary>Raw panel boxes, as page fractions, before ordering.</summary>
    public List<PanelRect> Run(SKBitmap page)
    {
        int w = page.Width;
        int h = page.Height;
        if (w < 16 || h < 16)
        {
            return [];
        }

        double scale = InputSize / (double)Math.Max(w, h);
        int nw = Math.Max(1, (int)Math.Round(w * scale));
        int nh = Math.Max(1, (int)Math.Round(h * scale));
        int padX = (InputSize - nw) / 2;
        int padY = (InputSize - nh) / 2;

        using var resized = page.Resize(new SKImageInfo(nw, nh, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return [];
        }

        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        var span = input.Buffer.Span;
        int plane = InputSize * InputSize;
        span.Fill(PadValue / 255f);
        var pixels = resized.Pixels;
        for (int y = 0; y < nh; y++)
        {
            int row = ((y + padY) * InputSize) + padX;
            for (int x = 0; x < nw; x++)
            {
                var c = pixels[(y * nw) + x];
                // Transparent pixels count as white paper.
                float a = c.Alpha / 255f;
                span[row + x] = ((c.Red / 255f) * a) + (1 - a);
                span[plane + row + x] = ((c.Green / 255f) * a) + (1 - a);
                span[(2 * plane) + row + x] = ((c.Blue / 255f) * a) + (1 - a);
            }
        }

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
        var output = results.First().AsTensor<float>();
        int count = output.Dimensions[1];
        int stride = output.Dimensions[2];
        var data = output.ToArray();

        var boxes = new List<PanelRect>();
        for (int i = 0; i < count; i++)
        {
            int o = i * stride;
            float score = data[o + 4];
            int cls = (int)Math.Round(data[o + 5]);
            if (cls != PanelClass || score < MinScore)
            {
                continue;
            }

            double x1 = Math.Clamp((data[o] - padX) / scale / w, 0, 1);
            double y1 = Math.Clamp((data[o + 1] - padY) / scale / h, 0, 1);
            double x2 = Math.Clamp((data[o + 2] - padX) / scale / w, 0, 1);
            double y2 = Math.Clamp((data[o + 3] - padY) / scale / h, 0, 1);
            if (x2 - x1 < MinSide || y2 - y1 < MinSide)
            {
                continue;
            }

            boxes.Add(new PanelRect(x1, y1, x2 - x1, y2 - y1));
        }

        return boxes;
    }

    /// <summary>Boxes below this height, as a fraction of the page, are not worth a panel when found as a gap between detected panels.</summary>
    public const double MinBandHeight = 0.08;

    /// <summary>A gap band only becomes a panel when its luminance varies at least this much (a blank margin does not).</summary>
    public const double MinBandContrast = 22;

    /// <summary>
    /// The model finds framed panels; a borderless wide panel between two framed rows is left uncovered. Any horizontal band of the page that no detected panel touches, tall enough and with
    /// real drawing in it, is added as a panel spanning the page width. Page margins (blank bands) are rejected by their flat luminance.
    /// </summary>
    public static List<PanelRect> FindBorderlessBands(SKBitmap page, IReadOnlyList<PanelRect> boxes)
    {
        var found = new List<PanelRect>();

        // Full-width bands between (or above/below) detected rows.
        var covered = boxes.OrderBy(b => b.Y).ToList();
        double cursor = 0;
        foreach (var b in covered)
        {
            if (b.Y - cursor >= MinBandHeight)
            {
                TryAddRegion(page, 0, 1, cursor, b.Y, found);
            }

            cursor = Math.Max(cursor, b.Bottom);
        }

        if (1 - cursor >= MinBandHeight)
        {
            TryAddRegion(page, 0, 1, cursor, 1, found);
        }

        // Side gaps inside a row of detected panels (a borderless panel beside framed ones).
        foreach (var row in PanelOrdering.Rows(boxes))
        {
            double top = row.Min(r => r.Y);
            double bottom = row.Max(r => r.Bottom);
            double x = 0;
            foreach (var r in row.OrderBy(r => r.X))
            {
                if (r.X - x >= MinBandWidth)
                {
                    TryAddRegion(page, x, r.X, top, bottom, found);
                }

                x = Math.Max(x, r.Right);
            }

            if (1 - x >= MinBandWidth)
            {
                TryAddRegion(page, x, 1, top, bottom, found);
            }
        }

        return found;
    }

    /// <summary>Side gaps narrower than this fraction of the page width are not worth a panel.</summary>
    public const double MinBandWidth = 0.20;

    private static void TryAddRegion(SKBitmap page, double left, double right, double top, double bottom, List<PanelRect> found)
    {
        int x0 = (int)Math.Round(left * page.Width);
        int x1 = Math.Min(page.Width, (int)Math.Round(right * page.Width));
        int y0 = (int)Math.Round(top * page.Height);
        int y1 = Math.Min(page.Height, (int)Math.Round(bottom * page.Height));
        if (y1 - y0 < 8 || x1 - x0 < 8)
        {
            return;
        }

        // Sample the region on a coarse grid; a real drawing has a wide luminance spread, a margin does not.
        int stepX = Math.Max(1, (x1 - x0) / 48);
        int stepY = Math.Max(1, (y1 - y0) / 48);
        double sum = 0, sumSq = 0;
        int n = 0;
        for (int y = y0; y < y1; y += stepY)
        {
            for (int x = x0; x < x1; x += stepX)
            {
                var c = page.GetPixel(x, y);
                double l = (0.299 * c.Red) + (0.587 * c.Green) + (0.114 * c.Blue);
                sum += l;
                sumSq += l * l;
                n++;
            }
        }

        if (n == 0)
        {
            return;
        }

        double mean = sum / n;
        double sd = Math.Sqrt(Math.Max(0, (sumSq / n) - (mean * mean)));
        if (sd >= MinBandContrast)
        {
            found.Add(new PanelRect(left, top, right - left, bottom - top));
        }
    }

    /// <summary>Orders raw boxes into reading order; an empty result means "nothing found" (whole page, unconfident).</summary>
    public static PagePanels ToPanels(IReadOnlyList<PanelRect> boxes, bool rightToLeft)
    {
        if (boxes.Count == 0)
        {
            return PagePanels.Whole;
        }

        return new PagePanels(PanelOrdering.Sort(boxes, rightToLeft), Confident: true);
    }

    public void Dispose() => _session.Dispose();
}
