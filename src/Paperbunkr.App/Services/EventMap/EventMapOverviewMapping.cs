using System;
using Avalonia;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>Projects map grid and viewport into the overview strip.</summary>
public static class EventMapOverviewMapping
{
    public static Rect TickRect(EventMapCell cell, EventMapLayoutResult layout, Size strip)
    {
        if (layout.ColumnCount == 0 || layout.TrackCount == 0) return default;
        double columnWidth = strip.Width / layout.ColumnCount;
        double bandHeight = Math.Max(0, strip.Height - 8);
        double rowHeight = bandHeight / layout.TrackCount;
        double y = 4 + Math.Floor(cell.Track * rowHeight);
        return new Rect(cell.Column * columnWidth, y, Math.Max(2, columnWidth), Math.Max(2, Math.Ceiling(rowHeight)));
    }

    public static Rect ViewportBox(Vector offset, Size viewport, EventMapLayoutResult layout, EventMapMetrics metrics, Size strip)
    {
        var total = layout.TotalSizeFor(metrics);
        if (total.Width <= 0 || total.Height <= 0) return default;
        double left = Math.Clamp(offset.X / total.Width, 0, 1);
        double top = Math.Clamp(offset.Y / total.Height, 0, 1);
        double right = Math.Clamp((offset.X + viewport.Width) / total.Width, left, 1);
        double bottom = Math.Clamp((offset.Y + viewport.Height) / total.Height, top, 1);
        double bandHeight = Math.Max(0, strip.Height - 8);
        return new Rect(left * strip.Width, 4 + (top * bandHeight), (right - left) * strip.Width, (bottom - top) * bandHeight);
    }

    public static Vector CenterOffsetFor(Point point, Size viewport, EventMapLayoutResult layout, EventMapMetrics metrics, Size strip)
    {
        var total = layout.TotalSizeFor(metrics);
        if (strip.Width <= 0 || strip.Height <= 0) return default;
        return new Vector(
            Math.Clamp((point.X / strip.Width * total.Width) - (viewport.Width / 2), 0, Math.Max(0, total.Width - viewport.Width)),
            Math.Clamp(((point.Y - 4) / Math.Max(1, strip.Height - 8) * total.Height) - (viewport.Height / 2), 0, Math.Max(0, total.Height - viewport.Height)));
    }
}
