using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// Draws everything on the Event Map that isn't a card, in one <see cref="Render"/> pass (docs/superpowers/specs/
/// 2026-09-25-event-map-design.md §3): alternating lane fills, column grid lines, and every edge whose column span meets
/// the visible range. Colours are resolved with <c>TryFindResource</c> at render time, so skin swaps and theme-variant
/// changes show on the next frame; the layer invalidates itself on both.
/// </summary>
public sealed class EventMapEdgeLayer : Control
{
    private static readonly ImmutableDashStyle TieInDash = new(new[] { 4.0, 3.0 }, 0);

    private EventMapLayoutResult? _layout;
    private IReadOnlySet<int>? _related;
    private Rect _viewport;

    // Render() allocates nothing in the steady state (avalonia-pro-max review checklist): edge geometry depends only on
    // the layout, pens and fills only on the skin, so both are cached and dropped when their input changes.
    private readonly Dictionary<EventMapEdge, Geometry> _geometries = new();
    private readonly Dictionary<(int Color, EventMapEdgeKind Kind, bool Related, bool Faded), IPen> _pens = new();
    private IBrush? _bgFill;
    private IBrush? _chromeFill;
    private IPen? _gridPen;
    private IBrush? _blockWash;

    public EventMapEdgeLayer()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => OnSkinChanged();
        ResourcesChanged += (_, _) => OnSkinChanged();
    }

    public EventMapLayoutResult? Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            _geometries.Clear();
            InvalidateVisual();
        }
    }

    private void OnSkinChanged()
    {
        _pens.Clear();
        _bgFill = null;
        _chromeFill = null;
        _gridPen = null;
        _blockWash = null;
        InvalidateVisual();
    }

    /// <summary>Cards that stay undimmed for the current selection; null = nothing selected, nothing dimmed.</summary>
    public IReadOnlySet<int>? RelatedSet
    {
        get => _related;
        set
        {
            _related = value;
            InvalidateVisual();
        }
    }

    /// <summary>The surface's effective viewport, in surface coordinates.</summary>
    public Rect Viewport
    {
        get => _viewport;
        set
        {
            if (_viewport != value)
            {
                _viewport = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Test seam: the pen an edge would be drawn with right now. Selection widens related edges 1.5× and fades the rest
    /// to 25 %; continuity edges are drawn at 35 %; tie-ins are dashed in the trunk colour.
    /// </summary>
    internal IPen PenFor(EventMapEdge edge)
    {
        bool related = _related is not null && _related.Contains(edge.From) && _related.Contains(edge.To);
        bool faded = _related is not null && !related;
        var key = (edge.ColorIndex, edge.Kind, related, faded);
        if (_pens.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var color = EventMapPalette.ResolveColor(this, edge.ColorIndex);
        double alpha = (edge.Kind == EventMapEdgeKind.Continuity ? 0.35 : 1.0) * (faded ? 0.25 : 1.0);
        double width = (edge.Kind == EventMapEdgeKind.TieIn ? 1.25 : 1.5) * (related ? 1.5 : 1.0);
        var pen = new ImmutablePen(
            new ImmutableSolidColorBrush(color, alpha),
            width,
            edge.Kind == EventMapEdgeKind.TieIn ? TieInDash : null,
            PenLineCap.Round,
            PenLineJoin.Round);
        _pens[key] = pen;
        return pen;
    }

    private Geometry GeometryFor(EventMapLayoutResult layout, EventMapEdge edge)
    {
        if (!_geometries.TryGetValue(edge, out var geometry))
        {
            geometry = BuildPath(layout.EdgePoints(edge), EventMapLayoutResult.EdgeCornerRadius);
            _geometries[edge] = geometry;
        }

        return geometry;
    }

    public override void Render(DrawingContext context)
    {
        var layout = _layout;
        if (layout is null || layout.IsEmpty)
        {
            return;
        }

        var range = layout.VisibleRange(_viewport);
        if (range.IsEmpty)
        {
            return;
        }

        var m = layout.Metrics;
        var total = layout.TotalSize;
        double left = Math.Max(0, _viewport.X);
        double right = Math.Min(total.Width, _viewport.Right);

        // Lane fills alternate between the page background and the chrome surface.
        var bg = _bgFill ??= new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbBgColor"));
        var chrome = _chromeFill ??= new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbChromeColor"));
        for (int t = range.FirstTrack; t <= range.LastTrack; t++)
        {
            context.FillRectangle(t % 2 == 0 ? bg : chrome, new Rect(left, t * m.TrackHeight, Math.Max(0, right - left), m.TrackHeight));
        }

        // Continuity map: every other event block gets a faint accent wash, so events read as bands down the whole map.
        if (layout.IsBlockMode)
        {
            var wash = _blockWash ??= new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor"), 0.05);
            int eventOrdinal = 0;
            foreach (var block in layout.Blocks)
            {
                if (block.Kind != EventMapBlockKind.Event)
                {
                    continue;
                }

                var (first, last) = layout.BlockColumns(block);
                if (eventOrdinal++ % 2 == 1 && last >= range.FirstColumn && first <= range.LastColumn)
                {
                    context.FillRectangle(wash, new Rect(first * m.SlotWidth, 0, (last - first + 1) * m.SlotWidth, total.Height));
                }
            }
        }

        // Column grid lines.
        var grid = _gridPen ??= new ImmutablePen(new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbBorderColor"), 0.5), 1);
        double top = range.FirstTrack * m.TrackHeight;
        double bottom = (range.LastTrack + 1) * m.TrackHeight;
        for (int c = range.FirstColumn; c <= range.LastColumn + 1; c++)
        {
            double x = Math.Round(c * m.SlotWidth) + 0.5;
            context.DrawLine(grid, new Point(x, top), new Point(x, bottom));
        }

        // Edges: faint ones first so related edges draw on top.
        for (int pass = 0; pass < 2; pass++)
        {
            bool relatedPass = pass == 1;
            foreach (var edge in layout.Edges)
            {
                if (edge.MaxColumn < range.FirstColumn || edge.MinColumn > range.LastColumn)
                {
                    continue;
                }

                bool isRelated = _related is null || (_related.Contains(edge.From) && _related.Contains(edge.To));
                if (isRelated != relatedPass)
                {
                    continue;
                }

                context.DrawGeometry(null, PenFor(edge), GeometryFor(layout, edge));
            }
        }
    }

    /// <summary>An orthogonal polyline with its interior corners rounded to <paramref name="radius"/> (clamped to half of each adjoining segment).</summary>
    internal static StreamGeometry BuildPath(IReadOnlyList<Point> points, double radius)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(points[0], isFilled: false);
        for (int i = 1; i < points.Count - 1; i++)
        {
            var prev = points[i - 1];
            var corner = points[i];
            var next = points[i + 1];
            double r = Math.Min(radius, Math.Min(Distance(prev, corner), Distance(corner, next)) / 2);
            var a = corner + (Direction(corner, prev) * r);
            var b = corner + (Direction(corner, next) * r);
            ctx.LineTo(a);
            ctx.QuadraticBezierTo(corner, b);
        }

        ctx.LineTo(points[^1]);
        ctx.EndFigure(isClosed: false);
        return geometry;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static Vector Direction(Point from, Point to)
    {
        double d = Distance(from, to);
        return d <= 0 ? default : new Vector((to.X - from.X) / d, (to.Y - from.Y) / d);
    }
}
