using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

public sealed partial class EventMapCanvas
{
    internal (int Cells, int Edges, int ColumnLabels) LastFrame { get; private set; }
    private readonly Dictionary<string, IBrush> _brushes = new();
    private readonly Dictionary<int, IBrush> _laneBrushes = new();
    private readonly Dictionary<int, IPen> _lanePens = new();
    private readonly Dictionary<EventMapEdge, Geometry> _edgeGeometries = new();
    private readonly Dictionary<(int Color, bool Related, bool Selected, int Progress), IPen> _edgePens = new();
    private EventMapVisibleRange _coverRange = EventMapVisibleRange.Empty;
    private EventMapVisibleRange _coverRailRange = EventMapVisibleRange.Empty;
    private int _coverMode = -1;
    private StreamGeometry? _gridGeometry;
    private int _gridFirst = -1, _gridLast = -1;
    private double _gridScale, _gridHeight;

    private void ClearRenderCaches()
    {
        _textCache.Clear();
        _brushes.Clear();
        _laneBrushes.Clear();
        _lanePens.Clear();
        _edgePens.Clear();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var layout = Layout;
        if (layout is null || layout.IsEmpty) { LastFrame = default; return; }
        var m = Metrics;
        var viewport = ContentViewport;
        var range = layout.VisibleRange(new Rect(new Point(Offset.X, Offset.Y), viewport), m);
        var look = EventMapCardLooks.For(m.CardWidth);
        if (Cards is { } allCards && _coverSource is { } coverSource)
        {
            var margin = layout.VisibleRange(new Rect(Offset.X - viewport.Width, Offset.Y - viewport.Height,
                viewport.Width * 3, viewport.Height * 3), m);
            int mode = (look >= EventMapCardLook.Standard ? 1 : 0) | (!IsLaneRailWide && m.TrackHeight >= 24 ? 2 : 0);
            if (margin != _coverRange || range != _coverRailRange || mode != _coverMode)
            {
                var railCards = (mode & 2) != 0
                    ? Enumerable.Range(Math.Max(0, range.FirstTrack), range.IsEmpty ? 0 : range.LastTrack - range.FirstTrack + 1)
                        .SelectMany(t => layout.CellsOnTrack(t).Take(1)) : Enumerable.Empty<int>();
                var cardIndices = (mode & 1) != 0 ? layout.CellsIn(margin) : Enumerable.Empty<int>();
                coverSource.SetWanted(cardIndices.Concat(railCards).Where(i => i < allCards.Count)
                    .Select(i => allCards[i].CoverKey).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!));
                _coverRange = margin;
                _coverRailRange = range;
                _coverMode = mode;
            }
        }
        int cellsDrawn = 0, edgesDrawn = 0, labelsDrawn = 0;
        IBrush Brush(string key)
        {
            if (!_brushes.TryGetValue(key, out var brush)) _brushes[key] = brush = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, key));
            return brush;
        }
        IBrush Lane(int color)
        {
            if (!_laneBrushes.TryGetValue(color, out var brush)) _laneBrushes[color] = brush = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, color));
            return brush;
        }
        IPen LanePen(int color)
        {
            if (!_lanePens.TryGetValue(color, out var pen)) _lanePens[color] = pen = new Pen(Lane(color), 1);
            return pen;
        }
        var background = Brush("PbBgColor");
        var chrome = Brush("PbChromeColor");
        var surface = Brush("PbSurface1Color");
        var border = new Pen(Brush("PbBorderColor"), 1);
        var textBrush = Brush("PbTextColor");
        var muted = Brush("PbTextMutedColor");
        var accent = Brush("PbAccentColor");
        var typeface = new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this));

        using (context.PushClip(new Rect(RailWidth, RulerHeight, viewport.Width, viewport.Height)))
        using (context.PushTransform(Matrix.CreateTranslation(RailWidth - Offset.X, RulerHeight - Offset.Y)))
        {
            var total = layout.TotalSizeFor(m);
            for (int t = range.FirstTrack; t <= range.LastTrack && !range.IsEmpty; t++)
            {
                context.FillRectangle(t % 2 == 0 ? background : chrome,
                    new Rect(Offset.X, t * m.TrackHeight, viewport.Width, m.TrackHeight));
                if (_highlightTrack == t && _highlightOpacity > 0)
                    using (context.PushOpacity(_highlightOpacity)) context.FillRectangle(Brush("PbAccentSoftColor"), new Rect(Offset.X, t * m.TrackHeight, viewport.Width, m.TrackHeight));
            }
            int ordinal = 0;
            var wash = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor"), 0.05);
            foreach (var block in layout.Blocks)
            {
                if (block.Kind is not (EventMapBlockKind.Event or EventMapBlockKind.Unplaced)) continue;
                var (first, last) = layout.BlockColumns(block);
                if (ordinal++ % 2 == 1 && last >= range.FirstColumn && first <= range.LastColumn)
                    context.FillRectangle(wash, new Rect(first * m.SlotWidth, 0, (last - first + 1) * m.SlotWidth, total.Height));
            }
            if (!range.IsEmpty)
            {
                if (_gridGeometry is null || _gridFirst != range.FirstColumn || _gridLast != range.LastColumn || _gridScale != Scale || _gridHeight != total.Height)
                {
                    _gridGeometry = new StreamGeometry();
                    using (var gridPath = _gridGeometry.Open())
                    {
                        for (int c = range.FirstColumn; c <= range.LastColumn + 1; c++)
                        {
                            gridPath.BeginFigure(new Point(c * m.SlotWidth, 0), false);
                            gridPath.LineTo(new Point(c * m.SlotWidth, total.Height));
                            gridPath.EndFigure(false);
                        }
                    }
                    _gridFirst = range.FirstColumn; _gridLast = range.LastColumn; _gridScale = Scale; _gridHeight = total.Height;
                }
                context.DrawGeometry(null, border, _gridGeometry);
            }
            // The "read so far" line (M6b §1): edges into read cards draw solid accent, the rest thin and faded.
            var readEdges = ReadEdges(layout);
            var edges = layout.Edges;
            for (int e = 0; e < edges.Count; e++)
            {
                var edge = edges[e];
                if (edge.MaxColumn < range.FirstColumn || edge.MinColumn > range.LastColumn) continue;
                if (look == EventMapCardLook.Dots && edge.From != SelectedIndex && edge.To != SelectedIndex) continue;
                bool related = RelatedSet is null || (RelatedSet.Contains(edge.From) && RelatedSet.Contains(edge.To));
                bool emphasised = related && RelatedSet is not null;
                bool ghostEdge = Cards is { } edgeCards && ((edge.From < edgeCards.Count && edgeCards[edge.From].IsGhost) || (edge.To < edgeCards.Count && edgeCards[edge.To].IsGhost));
                int progress = ghostEdge ? 3 : readEdges is null ? 0 : readEdges[e] ? 1 : 2;
                var penKey = (edge.ColorIndex, related, RelatedSet is not null, progress);
                if (!_edgePens.TryGetValue(penKey, out var pen))
                {
                    var color = EventMapPalette.ResolveColor(this, edge.ColorIndex);
                    _edgePens[penKey] = pen = progress switch
                    {
                        1 => new Pen(new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor")), emphasised ? 2.75 : 2),
                        2 => new Pen(new ImmutableSolidColorBrush(color, 0.35), emphasised ? 1.5 : 1),
                        // A chain into or out of a missing issue (M4): dashed, in muted ink.
                        3 => new Pen(new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbTextMutedColor"), 0.6), 1, new DashStyle(new double[] { 3, 3 }, 0)),
                        _ => new Pen(new ImmutableSolidColorBrush(color), emphasised ? 2.25 : 1.5),
                    };
                }
                Geometry? geometry;
                if (TraceEdge(edge) is { } trace)
                {
                    // Trace on select (M6b §3): the edge draws in from the end nearer the selected card.
                    if (trace.Fraction <= 0) continue;
                    IReadOnlyList<Point> points = layout.EdgePoints(edge, m);
                    if (trace.Reverse) points = points.Reverse().ToArray();
                    geometry = EventMapEdgeLayer.BuildPath(EventMapTrace.Trim(points, trace.Fraction), Math.Min(7, m.SlotWidth / 8));
                }
                else if (!_edgeGeometries.TryGetValue(edge, out geometry))
                    _edgeGeometries[edge] = geometry = EventMapEdgeLayer.BuildPath(layout.EdgePoints(edge, m), Math.Min(7, m.SlotWidth / 8));
                using (context.PushOpacity(Math.Min(CardOpacity(edge.From), CardOpacity(edge.To))))
                    context.DrawGeometry(null, pen, geometry);
                edgesDrawn++;
            }
            if (look == EventMapCardLook.Dots)
                cellsDrawn = DrawDots(context, layout, m, range, new Rect(new Point(Offset.X, Offset.Y), viewport), Lane, LanePen, accent);
            else foreach (int index in layout.CellsIn(range))
            {
                if (Cards is null || index >= Cards.Count) continue;
                var card = Cards[index];
                var rect = layout.CardRect(index, m);
                if (!rect.Intersects(new Rect(new Point(Offset.X, Offset.Y), viewport))) continue;
                using var opacity = context.PushOpacity(CardOpacity(index) * FeatureOpacity(index));
                var lane = BarBrush(index) ?? Lane(card.ColorIndex);
                if (look == EventMapCardLook.Dots)
                {
                    context.DrawRectangle(card.IsUnread ? null : lane, LanePen(card.ColorIndex), rect, 2, 2);
                }
                else if (card.IsGhost)
                {
                    DrawGhostCard(context, card, rect, look, typeface, lane, textBrush, muted);
                }
                else
                {
                    context.DrawRectangle(surface, border, rect, 3, 3);
                    if (_hoveredIndex == index)
                    {
                        using var hoverOpacity = context.PushOpacity(_hoverProgress);
                        context.FillRectangle(Brush("PbSurface2Color"), rect);
                    }
                    context.FillRectangle(lane, new Rect(rect.X, rect.Y, Math.Min(4, rect.Width), rect.Height));
                    double textLeft = rect.X + 7;
                    if (look >= EventMapCardLook.Standard)
                    {
                        var coverRect = look == EventMapCardLook.Covers
                            ? new Rect(rect.X + 4, rect.Y + 4, Math.Max(0, rect.Width - 8), Math.Max(0, rect.Height - 32))
                            : new Rect(rect.X + 6, rect.Y + 4, Math.Min(36, rect.Width / 3), Math.Max(0, rect.Height - 8));
                        context.FillRectangle(Brush("PbSurface2Color"), coverRect);
                        if (_coverSource?.Get(card.CoverKey, look == EventMapCardLook.Covers ? 256 : 48) is { } bitmap)
                            context.DrawImage(bitmap, new Rect(bitmap.Size), coverRect);
                        if (look == EventMapCardLook.Standard) textLeft = coverRect.Right + 5;
                    }
                    string label = look is EventMapCardLook.Chips or EventMapCardLook.Compact ? card.Badge : card.Title;
                    double labelY = look == EventMapCardLook.Covers ? rect.Bottom - 25 : rect.Y + 4;
                    double glyphSpace = look >= EventMapCardLook.Compact ? 18 : 5;
                    DrawText(context, label, new Rect(textLeft, labelY, Math.Max(0, rect.Right - textLeft - glyphSpace), 16), typeface, textBrush, 11);
                    if (look == EventMapCardLook.Standard)
                        DrawText(context, card.ReadStateLabel + (card.HasCoverDate ? " · " + card.CoverDate : ""),
                            new Rect(textLeft, rect.Bottom - 19, Math.Max(0, rect.Right - textLeft - 5), 14), typeface, muted, 10);
                    DrawFace(context, card, rect, look);
                    if (look >= EventMapCardLook.Compact)
                    {
                        var center = new Point(rect.Right - 9, look == EventMapCardLook.Covers ? rect.Bottom - 10 : rect.Y + 10);
                        var statePen = new Pen(card.IsRead ? accent : muted, 1.2);
                        if (card.IsRead)
                        {
                            context.DrawLine(statePen, center + new Vector(-4, 0), center + new Vector(-1, 3));
                            context.DrawLine(statePen, center + new Vector(-1, 3), center + new Vector(4, -3));
                        }
                        else context.DrawEllipse(card.IsInProgress ? accent : null, statePen, center, 3.5, 3.5);
                    }
                }
                if (SelectedIndex == index)
                    context.DrawRectangle(null, new Pen(accent, IsKeyboardFocusWithin ? 2 : 1), rect.Inflate(2), 3, 3);
                cellsDrawn++;
            }
            DrawBrackets(context, layout, m, look, range, typeface);
            DrawYouAreHere(context, layout, m, look, new Rect(new Point(Offset.X, Offset.Y), viewport), accent);
        }

        context.FillRectangle(chrome, new Rect(0, RulerHeight, RailWidth, viewport.Height));
        using (context.PushClip(new Rect(0, RulerHeight, RailWidth, viewport.Height)))
        {
            for (int t = range.FirstTrack; t <= range.LastTrack && !range.IsEmpty; t++)
            {
                if (LaneHeaders is null || t >= LaneHeaders.Count) continue;
                var header = LaneHeaders[t];
                double y = RulerHeight + (t * m.TrackHeight) - Offset.Y;
                context.FillRectangle(Lane(header.ColorIndex), new Rect(0, y + 2, 4, Math.Max(0, m.TrackHeight - 4)));
                if (m.TrackHeight < 24) continue;
                if (layout.Tracks[t].IsFolded)
                    DrawText(context, "›", new Rect(RailWidth - 15, y + 3, 12, 16), typeface, textBrush, 14);
                if (IsLaneRailWide)
                {
                    double chipWidth = header.RelationLabel is null ? 0 : Math.Min(64, 12 + header.RelationLabel.Length * 6);
                    DrawText(context, header.Label, new Rect(10, y + Math.Max(0, (m.TrackHeight - 28) / 2), Math.Max(0, RailWidth - 16 - chipWidth), 14), typeface, textBrush, 11);
                    if (header.RelationLabel is { } relation)
                    {
                        var chip = new Rect(RailWidth - chipWidth - 4, y + Math.Max(0, (m.TrackHeight - 28) / 2), chipWidth, 14);
                        context.DrawRectangle(Brush("PbSurface2Color"), new Pen(Brush("PbBorderColor"), 1), chip, 3, 3);
                        DrawText(context, relation, new Rect(chip.X + 3, chip.Y, Math.Max(0, chip.Width - 6), chip.Height), typeface, muted, 10);
                    }
                    DrawText(context, header.CountLabel, new Rect(10, y + Math.Max(0, (m.TrackHeight - 28) / 2) + 14, RailWidth - 16, 14), typeface, muted, 10);
                }
                else
                {
                    var indices = layout.CellsOnTrack(t);
                    var coverRect = new Rect(10, y + 2, 32, Math.Min(48, Math.Max(0, m.TrackHeight - 16)));
                    context.FillRectangle(Brush("PbSurface2Color"), coverRect);
                    if (indices.Count > 0 && Cards is { } cards && indices[0] < cards.Count &&
                        _coverSource?.Get(cards[indices[0]].CoverKey, 48) is { } thumb)
                        context.DrawImage(thumb, new Rect(thumb.Size), coverRect);
                    DrawText(context, header.IsTrunk ? "Spine" : header.Count.ToString(CultureInfo.CurrentCulture),
                        new Rect(7, coverRect.Bottom, RailWidth - 10, 13), typeface, muted, 10);
                }
                if (Cards is { } progressCards)
                {
                    var indices = layout.CellsOnTrack(t).Where(i => i < progressCards.Count).ToArray();
                    if (indices.Length > 0)
                    {
                        double width = Math.Max(0, RailWidth - 14), py = y + m.TrackHeight - 3;
                        double readWidth = width * indices.Count(i => progressCards[i].IsRead) / indices.Length;
                        double activeWidth = width * indices.Count(i => progressCards[i].IsInProgress) / indices.Length;
                        context.FillRectangle(Brush("PbBorderColor"), new Rect(7, py, width, 2));
                        context.FillRectangle(Brush("PbSuccessColor"), new Rect(7, py, readWidth, 2));
                        context.FillRectangle(accent, new Rect(7 + readWidth, py, activeWidth, 2));
                    }
                }
            }
        }
        context.FillRectangle(chrome, new Rect(0, 0, Bounds.Width, RulerHeight));
        using (context.PushClip(new Rect(RailWidth, 0, viewport.Width, RulerHeight)))
        {
            foreach (var block in layout.Blocks)
            {
                var (first, last) = layout.BlockColumns(block);
                double left = RailWidth + (first * m.SlotWidth) - Offset.X;
                double width = (last - first + 1) * m.SlotWidth;
                if (left + width < RailWidth || left > Bounds.Width) continue;
                if (block.Kind == EventMapBlockKind.Event && block.EventId is int bandEvent)
                    DrawBandArt(context, bandEvent, new Rect(left, 0, width, RulerHeight));
                DrawText(context, block.Label, new Rect(left + 4, 2, Math.Max(0, width - 8), 14), typeface, muted, 10);
                context.DrawLine(border, new Point(left, 0), new Point(left, RulerHeight));
                if (block.Kind is EventMapBlockKind.Event or EventMapBlockKind.Unplaced && Cards is { } bandCards)
                {
                    var members = layout.Cells.Where(c => c.Row.BlockIndex == block.Index && c.Index < bandCards.Count).ToArray();
                    if (members.Length > 0)
                        context.FillRectangle(Brush("PbSuccessColor"), new Rect(left, RulerHeight - 3, width * members.Count(c => bandCards[c.Index].IsRead) / members.Length, 3));
                }
            }
            if (layout.IsBlockMode && Connectors is { } connectors)
            {
                for (int i = 0; i < connectors.Count; i++)
                {
                    var connector = connectors[i];
                    if (connector.FromBlock < 0 || connector.ToBlock < 0 || connector.FromBlock >= layout.Blocks.Count || connector.ToBlock >= layout.Blocks.Count) continue;
                    var from = layout.BlockColumns(layout.Blocks[connector.FromBlock]);
                    var to = layout.BlockColumns(layout.Blocks[connector.ToBlock]);
                    double x1 = RailWidth + ((from.First + from.Last + 1) * m.SlotWidth / 2) - Offset.X;
                    double x2 = RailWidth + ((to.First + to.Last + 1) * m.SlotWidth / 2) - Offset.X;
                    if (Math.Max(x1, x2) < RailWidth || Math.Min(x1, x2) > Bounds.Width) continue;
                    double y = 25 + (i % 2 == 0 ? -2 : 3);
                    var pen = new Pen(accent, 1.25, connector.IsYours ? null : new DashStyle(new[] { 3d, 2d }, 0));
                    context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
                    if (connector.IsOrdered)
                    {
                        context.DrawLine(pen, new Point(x2, y), new Point(x2 - 5, y - 3));
                        context.DrawLine(pen, new Point(x2, y), new Point(x2 - 5, y + 3));
                    }
                    else
                    {
                        context.DrawLine(pen, new Point(x1, y - 3), new Point(x1, y + 3));
                        context.DrawLine(pen, new Point(x2, y - 3), new Point(x2, y + 3));
                    }
                    double center = (x1 + x2) / 2;
                    var labelRect = new Rect(center - 30, y - 7, 60, 14);
                    context.FillRectangle(chrome, labelRect);
                    DrawText(context, connector.Label, labelRect, typeface, muted, 9);
                }
            }
            else if (layout.IsSpineMode && Cards is { } spineCards)
            {
                foreach (var segment in layout.Segments)
                {
                    if (segment.TrunkCell is not int trunk || trunk >= spineCards.Count) continue;
                    int column = layout.Cells[trunk].Column;
                    if (column < range.FirstColumn || column > range.LastColumn) continue;
                    DrawText(context, spineCards[trunk].Badge,
                        new Rect(RailWidth + (column * m.SlotWidth) - Offset.X, 2, m.SlotWidth, 14), typeface, accent, 10);
                }
            }
            if (look >= EventMapCardLook.Compact)
            {
                for (int c = range.FirstColumn; c <= range.LastColumn && !range.IsEmpty; c++)
                {
                    if (layout.ColumnLabel(c) is not { } label) continue;
                    DrawText(context, label, new Rect(RailWidth + (c * m.SlotWidth) - Offset.X, RulerHeight - 14, m.SlotWidth, 14), typeface, muted, 10);
                    labelsDrawn++;
                }
            }
        }
        context.DrawLine(border, new Point(RailWidth, 0), new Point(RailWidth, Bounds.Height));
        context.DrawLine(border, new Point(0, RulerHeight), new Point(Bounds.Width, RulerHeight));
        if (!IsLaneRailWide && _hoveredLaneIndex is int laneIndex && LaneHeaders is { } laneHeaders && laneIndex < laneHeaders.Count)
        {
            double top = RulerHeight + (laneIndex * m.TrackHeight) - Offset.Y;
            var flag = new Rect(RailWidth, Math.Clamp(top, RulerHeight, Math.Max(RulerHeight, Bounds.Height - 28)), Math.Min(220, viewport.Width), 28);
            using (context.PushOpacity(_hoverProgress))
            {
                context.DrawRectangle(Brush("PbSurface3Color"), border, flag, 4, 4);
                DrawText(context, laneHeaders[laneIndex].FullName, flag.Deflate(6), typeface, textBrush, 11);
            }
        }
        LastFrame = (cellsDrawn, edgesDrawn, labelsDrawn);
    }

    private void DrawText(DrawingContext context, string value, Rect bounds, Typeface typeface, IBrush brush, double size)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var color = (brush as ISolidColorBrush)?.Color ?? Colors.Gray;
        var text = _textCache.Get(value, typeface, size, bounds.Width, color);
        using (context.PushClip(bounds)) text.Draw(context, bounds.Position);
    }
}
