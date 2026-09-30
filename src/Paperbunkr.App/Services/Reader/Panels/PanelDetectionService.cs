using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>
/// The panel detection pipeline (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md): the ONNX model first, the gutter heuristic when the model is missing, fails to
/// load, or finds nothing, and the whole page (unconfident) when both give up. Tall webtoon strips take a separate path (<see cref="PanelDetector.DetectStrip"/>): both page detectors
/// resize the long side of what they are given, so an 800x15000 strip would shrink to a sliver, and the model boxes speech balloons on them.
/// Avalonia-free (Skia only) so the dev harness links it and measures the exact pipeline the reader runs.
/// </summary>
public static class PanelDetectionService
{
    /// <summary>A page taller than this multiple of its width is treated as a scrolling strip.</summary>
    public const double TallAspect = 2.4;

    private static readonly object Gate = new();
    private static OnnxPanelDetector? _onnx;
    private static bool _onnxTried;
    private static string? _modelPath;

    /// <summary>Where the model file lives. Defaults to <c>Models/panel-detector.onnx</c> next to the executable; the harness overrides it.</summary>
    public static string ModelPath
    {
        get => _modelPath ?? Path.Combine(AppContext.BaseDirectory, "Models", "panel-detector.onnx");
        set
        {
            lock (Gate)
            {
                _modelPath = value;
                _onnx?.Dispose();
                _onnx = null;
                _onnxTried = false;
            }
        }
    }

    /// <summary>False when the model was never loaded (missing file, unsupported runtime): the heuristic is doing all the work.</summary>
    public static bool OnnxAvailable => GetOnnx() is not null;

    /// <summary>Set to false to skip the model (used by the harness to measure the heuristic alone).</summary>
    public static bool UseOnnx { get; set; } = true;

    private static OnnxPanelDetector? GetOnnx()
    {
        if (!UseOnnx)
        {
            return null;
        }

        lock (Gate)
        {
            if (_onnxTried)
            {
                return _onnx;
            }

            _onnxTried = true;
            try
            {
                if (File.Exists(ModelPath))
                {
                    _onnx = new OnnxPanelDetector(ModelPath);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A missing native runtime, a corrupt model, an unsupported CPU: fall back to the heuristic for the rest of the session.
                _onnx = null;
            }

            return _onnx;
        }
    }

    /// <summary>Detects the panels of a decoded page. Never throws for an unreadable page: it returns <see cref="PagePanels.Whole"/>.</summary>
    public static PagePanels Detect(SKBitmap page, bool rightToLeft)
    {
        try
        {
            if (page.Width < 16 || page.Height < 16)
            {
                return PagePanels.Whole;
            }

            return page.Height > TallAspect * page.Width ? DetectTall(page, rightToLeft) : DetectPage(page, rightToLeft);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ObjectDisposedException or IOException)
        {
            return PagePanels.Whole;
        }
    }

    private static PagePanels DetectPage(SKBitmap page, bool rightToLeft)
    {
        var onnx = GetOnnx();
        if (onnx is not null)
        {
            try
            {
                var result = onnx.Detect(page, rightToLeft);
                if (result.Confident)
                {
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A failing session is treated like a missing one from now on.
                lock (Gate)
                {
                    _onnx?.Dispose();
                    _onnx = null;
                }
            }
        }

        return SkPanelAnalyzer.Analyze(page, rightToLeft);
    }

    /// <summary>Tall strips are cut at their empty bands (no model: it boxes speech balloons, and strips have no side-by-side panels); no bands means the whole page.</summary>
    private static PagePanels DetectTall(SKBitmap strip, bool rightToLeft) => SkPanelAnalyzer.AnalyzeStrip(strip);
}
