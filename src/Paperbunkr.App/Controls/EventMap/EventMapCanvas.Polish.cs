using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// M6b polish on the drawn map (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md): the "read so far" line, the "You are
/// here" ring and its slow pulse, cover art on continuity ruler event bands, and the outward trace when a card is selected.
/// </summary>
public sealed partial class EventMapCanvas
{
    public static readonly StyledProperty<bool> ShowProgressLineProperty =
        AvaloniaProperty.Register<EventMapCanvas, bool>(nameof(ShowProgressLine), true);

    /// <summary>Loads an event's cover for band art; replaced in tests. Runs off the UI thread.</summary>
    internal static Func<int, Bitmap?> BandArtLoader { get; set; } = StoryEventCoverCache.Get;

    /// <summary>Band art is drawn only on bands at least this wide on screen (§2).</summary>
    internal const double BandArtMinWidth = 80;

    private const int BandArtThumbWidth = 48;
    private const double PulseSeconds = 2.4;

    private readonly Dictionary<int, Bitmap?> _bandArt = new();
    private readonly HashSet<int> _bandArtPending = new();
    private bool _pulseRunning;
    private double _pulseOpacity = 0.8;
    private bool _ringDrawnLastFrame;
    private TopLevel? _pulseHost;

    private Dictionary<int, int>? _traceHops;
    private double _traceElapsedMs, _traceStaggerMs, _traceGrowMs;
    private int _traceGeneration;

    public bool ShowProgressLine { get => GetValue(ShowProgressLineProperty); set => SetValue(ShowProgressLineProperty, value); }

    /// <summary>The "You are here" card in the current frame (null when everything is read), for tests and the screen-reader name.</summary>
    internal int? NextUpIndex => Layout is { } layout && Cards is { } cards
        ? EventMapReadPath.NextUp(layout, i => i < cards.Count && cards[i].IsRead) : null;

    internal bool IsPulseRunning => _pulseRunning;

    internal bool IsTracing => _traceHops is not null;

    internal int BandArtCount => _bandArt.Count(p => p.Value is not null);

    /// <summary>Decode covers inline instead of in the background: an off-screen export has to have them all in its one render.</summary>
    internal void UseSynchronousCovers()
    {
        _coverSource?.Dispose();
        _coverSource = new EventMapCoverSource(InvalidateVisual, synchronous: true);
        _synchronousBandArt = true;
    }

    private bool _synchronousBandArt;

    // ===================== Missing issues (M4) =====================

    /// <summary>
    /// A ghost card: no fill, a dashed border (danger for "Not in your library", the border colour for "In your library"), the lane bar at
    /// 40 %, the badge or title and the ghost label where a member shows its read state. No read glyph, no cover.
    /// </summary>
    private void DrawGhostCard(DrawingContext context, ViewModels.EventMapCardViewModel card, Rect rect, EventMapCardLook look, Typeface typeface,
        IBrush lane, IBrush text, IBrush muted)
    {
        string borderKey = card.IsOwnedGhost ? "PbBorderColor" : "PbDangerColor";
        var dashed = new Pen(new SolidColorBrush(EventMapPalette.ResolveColor(this, borderKey)), 1.25, new DashStyle(new double[] { 4, 3 }, 0));
        context.DrawRectangle(new SolidColorBrush(EventMapPalette.ResolveColor(this, "PbBgColor")), dashed, rect, 3, 3);
        using (context.PushOpacity(0.4))
        {
            context.FillRectangle(lane, new Rect(rect.X, rect.Y, Math.Min(4, rect.Width), rect.Height));
        }

        double left = rect.X + 7;
        string label = look is EventMapCardLook.Chips or EventMapCardLook.Compact ? card.Badge : card.Title;
        DrawText(context, label, new Rect(left, rect.Y + 4, Math.Max(0, rect.Right - left - 5), 16), typeface, muted, 11);
        if (look >= EventMapCardLook.Standard)
        {
            DrawText(context, card.GhostLabel, new Rect(left, rect.Bottom - 19, Math.Max(0, rect.Right - left - 5), 14), typeface,
                card.IsOwnedGhost ? muted : new SolidColorBrush(EventMapPalette.ResolveColor(this, "PbDangerColor")), 10);
        }
    }

    // ===================== Read path =====================

    private bool[]? ReadEdges(EventMapLayoutResult layout) =>
        ShowProgressLine && Cards is { } cards ? EventMapReadPath.Classify(layout, i => i < cards.Count && cards[i].IsRead) : null;

    // ===================== You are here =====================

    private void DrawYouAreHere(DrawingContext context, EventMapLayoutResult layout, EventMapMetrics m, EventMapCardLook look, Rect visible, IBrush accent)
    {
        _ringDrawnLastFrame = false;
        if (look == EventMapCardLook.Dots || NextUpIndex is not int index)
        {
            return;
        }

        var rect = layout.CardRect(index, m).Inflate(3 + 1.5);
        if (!rect.Intersects(visible))
        {
            return;
        }

        _ringDrawnLastFrame = true;
        bool pulse = MotionPolicy.AllowsContinuous() && !MotionTokens.IsReducedMotion() && IsWindowActive();
        double opacity = pulse ? _pulseOpacity : 0.8;
        using (context.PushOpacity(opacity))
        {
            context.DrawRectangle(null, new Pen(accent, 3), rect, 5, 5);
        }

        if (pulse && !_pulseRunning)
        {
            StartPulse();
        }
    }

    private bool IsWindowActive() => TopLevel.GetTopLevel(this) is not Window window || window.IsActive;

    private void StartPulse()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _pulseRunning = true;
        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            // The clock only runs while the ring is on screen, the window is active and continuous motion is allowed.
            if (!_ringDrawnLastFrame || !IsEffectivelyVisible || !IsWindowActive() || !MotionPolicy.AllowsContinuous() || MotionTokens.IsReducedMotion())
            {
                _pulseRunning = false;
                _pulseOpacity = 0.8;
                InvalidateVisual();
                return;
            }

            started ??= now;
            double phase = ((now - started.Value).TotalSeconds % PulseSeconds) / PulseSeconds;
            _pulseOpacity = 0.35 + (0.55 * (0.5 - (0.5 * Math.Cos(phase * Math.PI * 2))));
            InvalidateVisual();
            top.RequestAnimationFrame(Frame);
        }

        top.RequestAnimationFrame(Frame);
    }

    private void HookPulseHost()
    {
        if (TopLevel.GetTopLevel(this) is Window window && !ReferenceEquals(window, _pulseHost))
        {
            UnhookPulseHost();
            _pulseHost = window;
            window.Activated += OnHostActivated;
        }
    }

    private void UnhookPulseHost()
    {
        if (_pulseHost is Window window)
        {
            window.Activated -= OnHostActivated;
        }

        _pulseHost = null;
    }

    private void OnHostActivated(object? sender, EventArgs e) => InvalidateVisual();

    // ===================== Band art =====================

    private void DrawBandArt(DrawingContext context, int eventId, Rect band)
    {
        if (band.Width < BandArtMinWidth || BandArt(eventId) is not { } art)
        {
            return;
        }

        bool light = ActualThemeVariant == ThemeVariant.Light;
        using (context.PushClip(band))
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            using (context.PushOpacity(0.30))
            {
                // Cover the band: scale the thumbnail to the band's width and centre it vertically. The upscale from 48 px is the blur.
                double height = band.Width * art.Size.Height / Math.Max(1, art.Size.Width);
                context.DrawImage(art, new Rect(art.Size), new Rect(band.X, band.Center.Y - (height / 2), band.Width, height));
            }

            context.FillRectangle(new SolidColorBrush(EventMapPalette.ResolveColor(this, "PbChromeColor"), light ? 0.75 : 0.55), band);
        }
    }

    private Bitmap? BandArt(int eventId)
    {
        if (_bandArt.TryGetValue(eventId, out var art))
        {
            return art;
        }

        if (_synchronousBandArt)
        {
            var inline = LoadBandArtThumb(BandArtLoader, eventId);
            _bandArt[eventId] = inline;
            return inline;
        }

        if (_bandArtPending.Add(eventId))
        {
            var loader = BandArtLoader;
            Task.Run(() => LoadBandArtThumb(loader, eventId)).ContinueWith(task => Dispatcher.UIThread.Post(() =>
            {
                _bandArtPending.Remove(eventId);
                var thumb = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                if (_coverSource is null)
                {
                    thumb?.Dispose();          // detached while loading
                    return;
                }

                _bandArt[eventId] = thumb;
                if (thumb is not null)
                {
                    InvalidateVisual();
                }
            }));
        }

        return null;
    }

    /// <summary>The event cover shrunk to a 48 px-wide thumbnail; drawn stretched, the upscale is the blur.</summary>
    private static Bitmap? LoadBandArtThumb(Func<int, Bitmap?> loader, int eventId)
    {
        try
        {
            using var full = loader(eventId);
            if (full is null || full.PixelSize.Width <= 0)
            {
                return null;
            }

            int height = Math.Max(1, (int)Math.Round(BandArtThumbWidth * (double)full.PixelSize.Height / full.PixelSize.Width));
            return full.CreateScaledBitmap(new PixelSize(BandArtThumbWidth, height), BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            return null;
        }
    }

    private void ClearBandArt()
    {
        foreach (var bitmap in _bandArt.Values)
        {
            bitmap?.Dispose();
        }

        _bandArt.Clear();
        _bandArtPending.Clear();
    }

    // ===================== Trace on select =====================

    /// <summary>Starts the outward trace for a new selection; returns false (plain dimming) when there is nothing to trace or motion is off.</summary>
    private bool StartTrace()
    {
        _traceHops = null;
        int generation = ++_traceGeneration;
        if (Layout is not { } layout || SelectedIndex is not int selected || RelatedSet is not { } related || !related.Contains(selected))
        {
            return false;
        }

        var duration = MotionTokens.Get(MotionRole.Travel).Move;
        if (MotionTokens.IsReducedMotion() || !MotionPolicy.AllowsMotion(MotionRole.Travel) || duration <= TimeSpan.Zero
            || TopLevel.GetTopLevel(this) is not { } top)
        {
            return false;
        }

        var hops = EventMapTrace.Hops(layout, selected, related);
        int lastHop = hops.Values.DefaultIfEmpty(0).Max();
        double total = duration.TotalMilliseconds;
        _traceStaggerMs = EventMapTrace.Stagger(lastHop, total * 0.6);
        _traceGrowMs = Math.Max(60, total - (lastHop * _traceStaggerMs));
        _traceElapsedMs = 0;
        _traceHops = hops;
        _previousRelated = null;
        _dimProgress = 1;

        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            if (generation != _traceGeneration)
            {
                return;
            }

            started ??= now;
            _traceElapsedMs = (now - started.Value).TotalMilliseconds;
            if (_traceElapsedMs >= total)
            {
                _traceHops = null;
            }

            InvalidateVisual();
            if (_traceHops is not null)
            {
                top.RequestAnimationFrame(Frame);
            }
        }

        top.RequestAnimationFrame(Frame);
        InvalidateVisual();
        return true;
    }

    /// <summary>While tracing: how much of an edge is drawn (0-1) and whether it is drawn from its target end. Null when not tracing.</summary>
    private (double Fraction, bool Reverse)? TraceEdge(EventMapEdge edge)
    {
        if (_traceHops is not { } hops || !hops.TryGetValue(edge.From, out int from) || !hops.TryGetValue(edge.To, out int to))
        {
            return null;
        }

        int hop = Math.Min(from, to);
        double fraction = Math.Clamp((_traceElapsedMs - (hop * _traceStaggerMs)) / _traceGrowMs, 0, 1);
        return (fraction, to < from);
    }

    /// <summary>While tracing: related cards lift to full opacity when their hop starts; everything else is dimmed at once.</summary>
    private double? TraceCardOpacity(int index)
    {
        if (_traceHops is not { } hops)
        {
            return null;
        }

        return hops.TryGetValue(index, out int hop) && _traceElapsedMs >= hop * _traceStaggerMs ? 1 : 0.25;
    }
}
