using System;

namespace Paperbunkr.App.Services.EventMap;

public enum EventMapCardLook
{
    Dots, Chips, Compact, Standard, Covers,
}

public static class EventMapCardLooks
{
    public static EventMapCardLook For(double width) => width switch
    {
        < 24 => EventMapCardLook.Dots,
        < 80 => EventMapCardLook.Chips,
        < 130 => EventMapCardLook.Compact,
        < 190 => EventMapCardLook.Standard,
        _ => EventMapCardLook.Covers,
    };
}

/// <summary>Screen-space geometry for the continuous map zoom.</summary>
public sealed record EventMapMetrics(double SlotWidth, double TrackHeight, double CardWidth, double CardHeight)
{
    public static EventMapMetrics ForScale(double scale)
    {
        double width = 150 * scale;
        double threshold = 190d / 150;
        double blend = Math.Clamp((scale - (threshold * 0.8)) / (threshold * 0.2), 0, 1);
        double coverHeight = (width * 1.5) + 28;
        double track = (84 * scale * (1 - blend)) + ((coverHeight + 12) * blend);
        double height = EventMapCardLooks.For(width) switch
        {
            EventMapCardLook.Dots => Math.Min(width, 12),
            EventMapCardLook.Covers => coverHeight,
            _ => width * 64 / 150,
        };
        return new EventMapMetrics(170 * scale, track, width, height);
    }
}
