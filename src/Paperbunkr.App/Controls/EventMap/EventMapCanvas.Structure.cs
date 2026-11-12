using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// M5 on the drawn map (docs/superpowers/specs/2026-11-11-continuity-map-structure-links-design.md): Featuring dimming, Colour by bars,
/// character faces and the creator-run / story-arc brackets under each lane.
/// </summary>
public sealed partial class EventMapCanvas
{
    public static readonly StyledProperty<IReadOnlySet<int>?> FeatureSetProperty =
        AvaloniaProperty.Register<EventMapCanvas, IReadOnlySet<int>?>(nameof(FeatureSet));

    public static readonly StyledProperty<IReadOnlyList<int>?> BarColorsProperty =
        AvaloniaProperty.Register<EventMapCanvas, IReadOnlyList<int>?>(nameof(BarColors));

    public static readonly StyledProperty<IReadOnlyList<MapBracketSpan>?> BracketsProperty =
        AvaloniaProperty.Register<EventMapCanvas, IReadOnlyList<MapBracketSpan>?>(nameof(Brackets));

    private readonly Dictionary<string, Bitmap?> _faces = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _facesPending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when a run bracket is clicked (its creator id), to highlight that creator.</summary>
    public event Action<int>? BracketCreatorPressed;

    public IReadOnlySet<int>? FeatureSet { get => GetValue(FeatureSetProperty); set => SetValue(FeatureSetProperty, value); }

    public IReadOnlyList<int>? BarColors { get => GetValue(BarColorsProperty); set => SetValue(BarColorsProperty, value); }

    public IReadOnlyList<MapBracketSpan>? Brackets { get => GetValue(BracketsProperty); set => SetValue(BracketsProperty, value); }

    internal int BracketsDrawn { get; private set; }

    /// <summary>Featuring and creator highlighting dim cards outside the set, on top of selection dimming.</summary>
    private double FeatureOpacity(int index) => FeatureSet is { } keep && !keep.Contains(index) ? 0.25 : 1;

    /// <summary>The card's bar brush under Colour by, or null for its lane colour.</summary>
    private IBrush? BarBrush(int index)
    {
        if (BarColors is not { } colors || index >= colors.Count)
        {
            return null;
        }

        string? key = colors[index] switch
        {
            EventMapViewModel.BarFaint => "PbTextFaintColor",
            EventMapViewModel.BarRead => "PbSuccessColor",
            EventMapViewModel.BarInProgress => "PbAccentColor",
            EventMapViewModel.BarUnread => "PbBorderColor",
            _ => null,
        };
        return new SolidColorBrush(key is null ? EventMapPalette.ResolveColor(this, colors[index]) : EventMapPalette.ResolveColor(this, key));
    }

    /// <summary>The face at a card's corner (§2): Standard 18 px top-right, Covers 24 px bottom-left over the cover with a 1 px ring.</summary>
    private void DrawFace(DrawingContext context, EventMapCardViewModel card, Rect rect, EventMapCardLook look)
    {
        if (look < EventMapCardLook.Standard || card.FacePath is not { } path || Face(path) is not { } face)
        {
            return;
        }

        double size = look == EventMapCardLook.Covers ? 24 : 18;
        var target = look == EventMapCardLook.Covers
            ? new Rect(rect.X + 8, rect.Bottom - 32 - size, size, size)
            : new Rect(rect.Right - size - 22, rect.Y + 4, size, size);
        double radius = 4;
        using (context.PushClip(new RoundedRect(target, radius)))
        {
            double scale = Math.Max(size / face.Size.Width, size / face.Size.Height);
            var drawn = new Size(face.Size.Width * scale, face.Size.Height * scale);
            context.DrawImage(face, new Rect(face.Size), new Rect(target.Center.X - (drawn.Width / 2), target.Center.Y - (drawn.Height / 2), drawn.Width, drawn.Height));
        }

        if (look == EventMapCardLook.Covers)
        {
            context.DrawRectangle(null, new Pen(new SolidColorBrush(EventMapPalette.ResolveColor(this, "PbBgColor")), 1), target, radius, radius);
        }
    }

    /// <summary>A face image decoded at 48 px off the UI thread and kept per canvas; nothing is downloaded.</summary>
    private Bitmap? Face(string path)
    {
        if (_faces.TryGetValue(path, out var bitmap))
        {
            return bitmap;
        }

        if (_facesPending.Add(path))
        {
            Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, 48, BitmapInterpolationMode.HighQuality);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return null;
                }
            }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
            {
                _facesPending.Remove(path);
                var decoded = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                if (_coverSource is null)
                {
                    decoded?.Dispose();
                    return;
                }

                _faces[path] = decoded;
                if (decoded is not null) InvalidateVisual();
            }));
        }

        return null;
    }

    private void ClearFaces()
    {
        foreach (var bitmap in _faces.Values) bitmap?.Dispose();
        _faces.Clear();
        _facesPending.Clear();
    }

    /// <summary>
    /// Brackets sit in the gap under a lane's cards, at Standard and larger: a run 2 px in its creator's colour 6 px below the cards (label at
    /// its start, trimmed), an arc 1.5 px in muted ink just under it (label only when there's no run on that lane).
    /// </summary>
    private void DrawBrackets(DrawingContext context, EventMapLayoutResult layout, EventMapMetrics m, EventMapCardLook look, EventMapVisibleRange range, Typeface typeface)
    {
        BracketsDrawn = 0;
        if (look < EventMapCardLook.Standard || Brackets is not { Count: > 0 } brackets)
        {
            return;
        }

        var runTracks = brackets.Where(b => b.Kind == MapBracketKind.Run).Select(b => b.Track).ToHashSet();
        var muted = new SolidColorBrush(EventMapPalette.ResolveColor(this, "PbTextMutedColor"));
        foreach (var bracket in brackets)
        {
            if (bracket.Track < range.FirstTrack || bracket.Track > range.LastTrack || bracket.LastColumn < range.FirstColumn || bracket.FirstColumn > range.LastColumn)
            {
                continue;
            }

            var (x1, x2, y) = BracketLine(bracket, m);
            IBrush brush = bracket.Kind == MapBracketKind.Run
                ? new SolidColorBrush(bracket.ColorIndex == EventMapViewModel.BarFaint
                    ? EventMapPalette.ResolveColor(this, "PbTextFaintColor") : EventMapPalette.ResolveColor(this, bracket.ColorIndex))
                : muted;
            var pen = new Pen(brush, bracket.Kind == MapBracketKind.Run ? 2 : 1.5);
            context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            context.DrawLine(pen, new Point(x1, y - 3), new Point(x1, y));
            context.DrawLine(pen, new Point(x2, y - 3), new Point(x2, y));
            bool labelled = bracket.Label is not null && (bracket.Kind == MapBracketKind.Run || !runTracks.Contains(bracket.Track));
            if (labelled)
            {
                DrawText(context, bracket.Label!, new Rect(x1 + 2, y - 12, Math.Max(0, x2 - x1 - 4), 11), typeface, muted, 9);
            }

            BracketsDrawn++;
        }
    }

    /// <summary>Content-space line for a bracket: from the first card's left to the last card's right, under the cards.</summary>
    private (double X1, double X2, double Y) BracketLine(MapBracketSpan bracket, EventMapMetrics m)
    {
        double inset = (m.SlotWidth - m.CardWidth) / 2;
        double x1 = (bracket.FirstColumn * m.SlotWidth) + inset;
        double x2 = ((bracket.LastColumn + 1) * m.SlotWidth) - inset;
        double cardBottom = (bracket.Track * m.TrackHeight) + ((m.TrackHeight + m.CardHeight) / 2);
        double gap = Math.Max(4, ((bracket.Track + 1) * m.TrackHeight) - cardBottom);
        double y = bracket.Kind == MapBracketKind.Run ? cardBottom + Math.Min(6, gap - 2) : cardBottom + Math.Max(2, gap - 1.5);
        return (x1, x2, y);
    }

    /// <summary>The bracket under a content-space point (for the hover tooltip and a run click), or null.</summary>
    internal MapBracketSpan? BracketAt(Point content)
    {
        if (Brackets is not { Count: > 0 } brackets || EventMapCardLooks.For(Metrics.CardWidth) < EventMapCardLook.Standard)
        {
            return null;
        }

        var m = Metrics;
        foreach (var bracket in brackets)
        {
            var (x1, x2, y) = BracketLine(bracket, m);
            if (content.X >= x1 && content.X <= x2 && Math.Abs(content.Y - y) <= 5)
            {
                return bracket;
            }
        }

        return null;
    }

    /// <summary>Hover over a bracket shows its tooltip; a click on a run bracket highlights its creator. Called from the input partial.</summary>
    internal bool HandleBracketPointer(Point viewPoint, bool pressed)
    {
        var content = new Point(viewPoint.X - RailWidth + Offset.X, viewPoint.Y - RulerHeight + Offset.Y);
        var bracket = viewPoint.X >= RailWidth && viewPoint.Y >= RulerHeight ? BracketAt(content) : null;
        string? tip = bracket?.Tooltip;
        if (!Equals(ToolTip.GetTip(this), tip) && (tip is not null || _bracketTipShown))
        {
            ToolTip.SetTip(this, tip);
            _bracketTipShown = tip is not null;
        }

        if (pressed && bracket is { Kind: MapBracketKind.Run, CreatorId: int creator })
        {
            BracketCreatorPressed?.Invoke(creator);
            return true;
        }

        return false;
    }

    private bool _bracketTipShown;
}
