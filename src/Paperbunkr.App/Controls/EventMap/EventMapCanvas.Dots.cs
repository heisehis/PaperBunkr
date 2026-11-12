using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Controls.EventMap;

public sealed partial class EventMapCanvas
{
    private sealed record DotBatch(int Color, bool Filled, int SampleIndex, int Count, StreamGeometry Geometry, bool Ghost = false, bool Featured = true);
    private readonly List<DotBatch> _dotBatches = new();
    private EventMapLayoutResult? _dotLayout;
    private EventMapVisibleRange _dotRange = EventMapVisibleRange.Empty;
    private double _dotScale;

    private void InvalidateDots() => _dotLayout = null;

    private int DrawDots(DrawingContext context, EventMapLayoutResult layout, EventMapMetrics metrics,
        EventMapVisibleRange range, Rect viewport, Func<int, IBrush> lane, Func<int, IPen> lanePen, IBrush accent)
    {
        if (!ReferenceEquals(_dotLayout, layout) || _dotRange != range || _dotScale != Scale)
        {
            var groups = new Dictionary<(int Color, bool Filled, bool Before, bool After, bool Ghost, bool Featured), List<int>>();
            foreach (int index in layout.CellsIn(range))
            {
                if (Cards is null || index >= Cards.Count) continue;
                var card = Cards[index];
                var key = (card.ColorIndex, !card.IsUnread && !card.IsGhost, _previousRelated is null || _previousRelated.Contains(index),
                    RelatedSet is null || RelatedSet.Contains(index), card.IsGhost, FeatureOpacity(index) >= 1);
                if (!groups.TryGetValue(key, out var indices)) groups[key] = indices = new List<int>();
                indices.Add(index);
            }
            _dotBatches.Clear();
            foreach (var (key, indices) in groups)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    foreach (int index in indices)
                    {
                        var rect = layout.CardRect(index, metrics);
                        double r = Math.Min(2, Math.Min(rect.Width, rect.Height) / 2);
                        path.BeginFigure(new Point(rect.Left + r, rect.Top), true);
                        path.LineTo(new Point(rect.Right - r, rect.Top));
                        path.QuadraticBezierTo(new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Top + r));
                        path.LineTo(new Point(rect.Right, rect.Bottom - r));
                        path.QuadraticBezierTo(new Point(rect.Right, rect.Bottom), new Point(rect.Right - r, rect.Bottom));
                        path.LineTo(new Point(rect.Left + r, rect.Bottom));
                        path.QuadraticBezierTo(new Point(rect.Left, rect.Bottom), new Point(rect.Left, rect.Bottom - r));
                        path.LineTo(new Point(rect.Left, rect.Top + r));
                        path.QuadraticBezierTo(new Point(rect.Left, rect.Top), new Point(rect.Left + r, rect.Top));
                        path.EndFigure(true);
                    }
                }
                _dotBatches.Add(new DotBatch(key.Color, key.Filled, indices[0], indices.Count, geometry, key.Ghost, key.Featured));
            }
            _dotLayout = layout;
            _dotRange = range;
            _dotScale = Scale;
        }
        int count = 0;
        // A missing issue (M4) is a dashed outline in the danger colour, never filled.
        var ghostPen = new Pen(new SolidColorBrush(EventMapPalette.ResolveColor(this, "PbDangerColor")), 1, new DashStyle(new double[] { 2, 2 }, 0));
        foreach (var batch in _dotBatches)
        {
            double opacity = CardOpacity(batch.SampleIndex) * (batch.Featured ? 1 : 0.25);
            IPen pen = batch.Ghost ? ghostPen : lanePen(batch.Color);
            if (opacity >= 1)
                context.DrawGeometry(batch.Filled ? lane(batch.Color) : null, pen, batch.Geometry);
            else
                using (context.PushOpacity(opacity))
                    context.DrawGeometry(batch.Filled ? lane(batch.Color) : null, pen, batch.Geometry);
            count += batch.Count;
        }
        if (SelectedIndex is int selected && selected >= 0 && selected < layout.Cells.Count)
        {
            var rect = layout.CardRect(selected, metrics).Inflate(2);
            if (rect.Intersects(viewport))
            {
                if (Cards is { } cards && selected < cards.Count)
                    context.DrawRectangle(cards[selected].IsUnread ? null : lane(cards[selected].ColorIndex), lanePen(cards[selected].ColorIndex), rect, 3, 3);
                context.DrawRectangle(null, new Pen(accent, 2), rect.Inflate(1), 3, 3);
            }
        }
        return count;
    }
}
