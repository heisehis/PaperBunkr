using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace Paperbunkr.App.Views;

/// <summary>Live config, sent whenever the overlay's size/theme changes.</summary>
internal sealed record MatrixRainConfig(Size Bounds, string[] Glyphs, string ColorHex);

/// <summary>Screen-space rects (already in this overlay's own coordinate space) to exclude from
/// drawing - the top-level opaque content regions registered via <see cref="MatrixRainOverlay"/>'s
/// attached property. Empty/null means draw everywhere (no exclusions registered yet).</summary>
internal sealed record MatrixRainExcludeRects(IReadOnlyList<Rect> Rects);

/// <summary>UI-thread signal (battery poll, IsVisible, reduced motion) - true = the frame loop
/// should be running, false = it should stop (and stay stopped until this arrives true again).</summary>
internal sealed record MatrixRainRunState(bool ShouldRun);

/// <summary>Sentinel - dispose SKPaint/SKTypeface now (IsVisible went false or detaching), don't wait for the GC finalizer.</summary>
internal sealed record MatrixRainDispose;

/// <summary>
/// Renders falling glyph columns (docs/superpowers/specs/2026-09-16-theme-system-design.md § Matrix
/// rain effect) - mirrors <see cref="ReaderPageVisualHandler"/>'s already-shipped shape exactly
/// (subclass <see cref="CompositionCustomVisualHandler"/>, <c>OnMessage</c>/<c>OnRender</c>/
/// <c>OnAnimationFrameUpdate</c>), not the generic docs sample's delegate-based construction - that
/// shape is unproven in this exact Avalonia version, this one is already working code in this
/// project. Runs on the compositor thread.
/// </summary>
internal sealed class MatrixRainVisualHandler : CompositionCustomVisualHandler
{
    private const float CellSize = 18f;
    private const int TrailLength = 18;
    private const float MaxAlpha = 140f;

    private struct Column
    {
        public float X;
        public float HeadY;
        public float Speed;
        public int Seed;
    }

    private Size _bounds;
    private string[] _glyphs = DefaultGlyphs;
    private SKColor _color = new(0x33, 0xFF, 0x66);
    private IReadOnlyList<Rect>? _excludeRects;

    // Test seam (mirrors ThemeService's internal-constructor convention, App.Tests-only via
    // InternalsVisibleTo): these four fields drive the run/dispose lifecycle that Paperbunkr.App.
    // Tests/MatrixRainVisualHandlerTests.cs exercises directly, without a live Compositor attach -
    // calling RegisterForNextAnimationFrameUpdate()/Invalidate() on an unattached handler throws
    // InvalidOperationException, so tests prime state here instead of driving it through OnMessage
    // alone.
    internal bool _shouldRun;
    internal bool _running;
    internal SKPaint? _paint;
    internal SKTypeface? _typeface;

    private TimeSpan? _lastFrameTime;
    private Column[] _columns = Array.Empty<Column>();
    private readonly Random _random = new();

    private static readonly string[] DefaultGlyphs =
    {
        "ア", "イ", "ウ", "エ", "オ", "カ", "キ", "ク", "ケ", "コ", "サ", "シ", "ス", "セ", "ソ", "0", "1",
    };

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case MatrixRainConfig config:
                bool sizeChanged = config.Bounds != _bounds;
                _bounds = config.Bounds;
                _glyphs = config.Glyphs.Length > 0 ? config.Glyphs : DefaultGlyphs;
                _color = SKColor.TryParse(config.ColorHex, out var parsed) ? parsed : new SKColor(0x33, 0xFF, 0x66);
                if (sizeChanged || _columns.Length == 0)
                {
                    RebuildColumns();
                }
                Invalidate();
                break;

            case MatrixRainExcludeRects exclude:
                _excludeRects = exclude.Rects;
                Invalidate();
                break;

            case MatrixRainRunState run:
                _shouldRun = run.ShouldRun;
                // Resuming after a stopped loop - real bug caught in review, now fixed: simply
                // flipping a flag back to true does nothing if OnAnimationFrameUpdate has already
                // stopped re-registering itself, since nothing is left running to observe the flag.
                // The UI-thread signal has to actively kick the loop again from here, on the
                // compositor thread, where RegisterForNextAnimationFrameUpdate actually belongs.
                if (_shouldRun && !_running)
                {
                    _running = true;
                    _lastFrameTime = null;
                    RegisterForNextAnimationFrameUpdate();
                }
                break;

            case MatrixRainDispose:
                _paint?.Dispose();
                _typeface?.Dispose();
                _paint = null;
                _typeface = null;
                break;
        }
    }

    private void RebuildColumns()
    {
        int columnCount = Math.Max(1, (int)(_bounds.Width / CellSize));
        _columns = new Column[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            _columns[i] = NewColumn(i, startAboveScreen: true);
        }
    }

    private Column NewColumn(int index, bool startAboveScreen)
    {
        return new Column
        {
            X = index * CellSize,
            HeadY = startAboveScreen ? (float)(-_random.NextDouble() * _bounds.Height) : 0f,
            Speed = 60f + (float)(_random.NextDouble() * 120f), // px/sec, frame-rate independent
            Seed = _random.Next(),
        };
    }

    /// <summary>
    /// Frame-rate independence (docs/superpowers/specs/2026-09-16-theme-system-design.md § Matrix
    /// rain effect) - advances by elapsed wall-clock time via <see cref="CompositionCustomVisualHandler.CompositionNow"/>,
    /// not a fixed per-frame step, so a 144Hz display doesn't visibly rain faster than a 60Hz one.
    /// Stops re-registering (the loop naturally terminates) as soon as <see cref="_shouldRun"/> is
    /// false - resuming requires a fresh <see cref="MatrixRainRunState"/> message, handled above.
    /// </summary>
    public override void OnAnimationFrameUpdate()
    {
        if (!_shouldRun)
        {
            _running = false;
            return;
        }

        double elapsedSeconds = _lastFrameTime is { } last ? (CompositionNow - last).TotalSeconds : 0;
        _lastFrameTime = CompositionNow;
        elapsedSeconds = Math.Min(elapsedSeconds, 0.1); // clamp a huge gap (e.g. resumed after being stopped) to one reasonable step

        float maxY = (float)_bounds.Height + (TrailLength * CellSize);
        for (int i = 0; i < _columns.Length; i++)
        {
            var column = _columns[i];
            column.HeadY += column.Speed * (float)elapsedSeconds;
            if (column.HeadY > maxY)
            {
                _columns[i] = NewColumn(i, startAboveScreen: false);
                _columns[i].HeadY = -(float)(_random.NextDouble() * 200);
            }
            else
            {
                _columns[i] = column;
            }
        }

        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override Rect GetRenderBounds() => new(_bounds);

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_bounds.Width <= 0 || _bounds.Height <= 0)
        {
            return;
        }

        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
        {
            return;
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        _paint ??= new SKPaint { IsAntialias = true, Color = _color };
        _typeface ??= SKTypeface.FromFamilyName("Cascadia Code") ?? SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default;
        _paint.Typeface = _typeface;
        _paint.TextSize = CellSize;

        int saveCount = canvas.Save();
        try
        {
            // Overdraw constraint (docs/superpowers/specs/2026-09-16-theme-system-design.md § Matrix
            // rain effect) - z-order hiding the rain behind an opaque panel stops the user from
            // seeing it, but not Skia from computing/rasterizing it. Clip out the registered
            // top-level opaque regions so those columns are skipped entirely, not just covered.
            if (_excludeRects is { Count: > 0 } excludeRects)
            {
                foreach (var rect in excludeRects)
                {
                    canvas.ClipRect(new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom), SKClipOperation.Difference);
                }
            }

            foreach (var column in _columns)
            {
                for (int row = 0; row < TrailLength; row++)
                {
                    float y = column.HeadY - (row * CellSize);
                    if (y < -CellSize || y > _bounds.Height)
                    {
                        continue;
                    }

                    // Deterministic per-cell glyph pick (stable across frames for a given
                    // column/row/seed, not re-randomized every frame - a static-looking trail with a
                    // moving head reads as "falling", constant flicker across the whole trail reads
                    // as noise). Modulo-then-add-then-modulo avoids a negative result without risking
                    // Math.Abs(int.MinValue)'s overflow edge case.
                    int glyphIndex = (((column.Seed + (row * 7)) % _glyphs.Length) + _glyphs.Length) % _glyphs.Length;
                    // Capped well below opaque: the rain now shows behind any bare-background text
                    // (card titles, subtitles), so even the bright leading glyph has to stay dim
                    // enough not to fight the theme's own text colors for legibility.
                    float fade = row == 0 ? 1f : 0.7f * (1f - (row / (float)TrailLength));
                    byte alpha = (byte)(fade * MaxAlpha);
                    _paint.Color = _color.WithAlpha(alpha);
                    canvas.DrawText(_glyphs[glyphIndex], column.X, y, _paint);
                }
            }
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }
}
