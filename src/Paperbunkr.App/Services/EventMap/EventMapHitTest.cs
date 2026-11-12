using Avalonia;

namespace Paperbunkr.App.Services.EventMap;

public enum EventMapHitKind { Empty, Card, Band, Lane, Corner }

public readonly record struct EventMapHit(EventMapHitKind Kind, int Index = -1);

/// <summary>Maps a canvas point to its pinned chrome or its scaled content.</summary>
public static class EventMapHitTest
{
    public static EventMapHit At(Point point, EventMapLayoutResult layout, EventMapMetrics metrics,
        Vector offset, double railWidth, double rulerHeight, int? selectedIndex = null)
    {
        if (point.X < 0 || point.Y < 0) return new(EventMapHitKind.Empty);
        if (point.X < railWidth && point.Y < rulerHeight) return new(EventMapHitKind.Corner);
        if (point.X < railWidth)
        {
            int track = (int)((point.Y - rulerHeight + offset.Y) / metrics.TrackHeight);
            return track >= 0 && track < layout.TrackCount ? new(EventMapHitKind.Lane, track) : new(EventMapHitKind.Empty);
        }
        double x = point.X - railWidth + offset.X;
        if (point.Y < rulerHeight)
        {
            int column = (int)(x / metrics.SlotWidth);
            foreach (var block in layout.Blocks)
            {
                var span = layout.BlockColumns(block);
                if (column >= span.First && column <= span.Last) return new(EventMapHitKind.Band, block.Index);
            }
            return new(EventMapHitKind.Empty);
        }
        var contentPoint = new Point(x, point.Y - rulerHeight + offset.Y);
        var range = layout.VisibleRange(new Rect(contentPoint, new Size(0.01, 0.01)), metrics);
        if (selectedIndex is int selected && selected >= 0 && selected < layout.Cells.Count &&
            EventMapCardLooks.For(metrics.CardWidth) == EventMapCardLook.Dots && layout.CardRect(selected, metrics).Inflate(2).Contains(contentPoint))
            return new(EventMapHitKind.Card, selected);
        foreach (int index in layout.CellsIn(range))
            if (layout.CardRect(index, metrics).Contains(contentPoint)) return new(EventMapHitKind.Card, index);
        return new(EventMapHitKind.Empty);
    }
}
