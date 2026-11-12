using System;
using Avalonia;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>Pure zoom calculations. Offsets are in scaled content pixels.</summary>
public static class EventMapZoom
{
    public const double AbsoluteMinimum = 0.06;
    public const double Maximum = 2;

    public static double ClampScale(double scale, double minimum) => Math.Clamp(scale, minimum, Maximum);

    /// <summary>Keep the initial Standard scale reachable even when a tiny map fits only above it.</summary>
    public static double MinimumScaleForFit(double fitScale) => Math.Min(1, fitScale);

    public static double FitScale(int columns, int tracks, Size viewport, double railWidth, double rulerHeight)
    {
        double width = Math.Max(0, viewport.Width - railWidth);
        double height = Math.Max(0, viewport.Height - rulerHeight);
        double low = AbsoluteMinimum;
        double high = Maximum;
        for (int i = 0; i < 60; i++)
        {
            double mid = (low + high) / 2;
            var metrics = EventMapMetrics.ForScale(mid);
            if ((columns * metrics.SlotWidth <= width) && (tracks * metrics.TrackHeight <= height))
                low = mid;
            else
                high = mid;
        }
        return low;
    }

    public static double FitBand(int columns, double viewportWidth) =>
        columns <= 0 ? Maximum : Math.Clamp(viewportWidth / (columns * 170), AbsoluteMinimum, Maximum);

    public static double ScaleForLook(EventMapCardLook look) => look switch
    {
        EventMapCardLook.Dots => 12d / 150,
        EventMapCardLook.Chips => 24d / 150,
        EventMapCardLook.Compact => 80d / 150,
        EventMapCardLook.Standard => 1,
        EventMapCardLook.Covers => 190d / 150,
        _ => 1,
    };

    public static double ToSlider(double scale, double minimum) =>
        minimum >= Maximum ? 0 : Math.Log(ClampScale(scale, minimum) / minimum) / Math.Log(Maximum / minimum);

    public static double FromSlider(double value, double minimum) =>
        minimum * Math.Pow(Maximum / minimum, Math.Clamp(value, 0, 1));

    public static (double Scale, Vector Offset) ZoomAbout(double scale, Vector offset, Point pointer, double factor, double minimum)
    {
        double next = ClampScale(scale * factor, minimum);
        double ratio = next / scale;
        return (next, new Vector(((offset.X + pointer.X) * ratio) - pointer.X,
            ((offset.Y + pointer.Y) * ratio) - pointer.Y));
    }
}
