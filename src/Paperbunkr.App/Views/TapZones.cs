using System;
using Avalonia;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>What a tap or click in a tap zone asks the reader to do (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 4).</summary>
public enum TapAction
{
    /// <summary>Nothing: no zone there, or zones are off for this input.</summary>
    None,

    /// <summary>Previous page / one screen back, in reading order (right-to-left reading has already been mirrored in).</summary>
    Previous,

    /// <summary>Next page / one screen forward, in reading order.</summary>
    Next,

    /// <summary>Spatial back: the left key. Turns the page whichever way the book reads (paged: <c>ExecuteTurn(forward: false)</c>).</summary>
    Left,

    /// <summary>Spatial forward: the right key (paged: <c>ExecuteTurn(forward: true)</c>).</summary>
    Right,

    /// <summary>The reader menu area: toggles the chrome for touch and pen, does nothing for the mouse.</summary>
    Menu,
}

/// <summary>Which kind of pointer made the tap (pen counts as touch).</summary>
public enum TapInput
{
    Mouse,
    Touch,
}

/// <summary>
/// Pure tap/click zone resolution. Layouts and region tables follow Mihon (<c>mihonapp/mihon</c> <c>ui/reader/viewer/navigation/*.kt</c>): each layout is an ordered list of
/// rectangles on the unit square mapped to previous / next (or spatial left / right); the first rectangle containing the point wins and an uncovered point is the menu area.
/// </summary>
internal static class TapZoneResolver
{
    private readonly record struct Region(double Left, double Top, double Right, double Bottom, TapAction Action)
    {
        public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    private static readonly Region[] LShaped =
    [
        new(0, 0.33, 0.33, 0.66, TapAction.Previous),
        new(0, 0, 1, 0.33, TapAction.Previous),
        new(0.66, 0.33, 1, 0.66, TapAction.Next),
        new(0, 0.66, 1, 1, TapAction.Next),
    ];

    private static readonly Region[] Kindlish =
    [
        new(0.33, 0.33, 1, 1, TapAction.Next),
        new(0, 0.33, 0.33, 1, TapAction.Previous),
    ];

    private static readonly Region[] Edge =
    [
        new(0, 0, 0.33, 1, TapAction.Next),
        new(0.33, 0.66, 0.66, 1, TapAction.Previous),
        new(0.66, 0, 1, 1, TapAction.Next),
    ];

    private static readonly Region[] RightAndLeft =
    [
        new(0, 0, 0.33, 1, TapAction.Left),
        new(0.66, 0, 1, 1, TapAction.Right),
    ];

    /// <summary>
    /// Resolves a tap at <paramref name="point"/> in a <paramref name="size"/>-sized canvas.
    /// <paramref name="continuous"/>: continuous and long-strip modes (where <see cref="TapZoneLayout.Default"/> means "no zones", today's behaviour).
    /// <paramref name="rightToLeft"/>: the book reads right to left (previous/next regions mirror; the spatial left/right layout does not).
    /// <paramref name="vertical"/>: paged top-to-bottom mode (only affects <see cref="TapZoneLayout.Default"/>, which splits on Y there).
    /// <paramref name="tapZonesForMouse"/>: when false a mouse ignores named layouts and keeps the plain halves (paged) or nothing (continuous).
    /// </summary>
    public static TapAction Resolve(Point point, Size size, TapZoneLayout layout, TapZoneInvert invert, TapInput input, bool tapZonesForMouse,
        bool rightToLeft, bool vertical, bool continuous)
    {
        if (size.Width <= 0 || size.Height <= 0 || layout == TapZoneLayout.Disabled)
        {
            return TapAction.None;
        }

        bool useDefault = layout == TapZoneLayout.Default || (input == TapInput.Mouse && !tapZonesForMouse);
        if (useDefault)
        {
            return continuous ? TapAction.None : ResolveDefault(point, size, input, vertical);
        }

        double x = Math.Clamp(point.X / size.Width, 0, 0.999999);
        double y = Math.Clamp(point.Y / size.Height, 0, 0.999999);

        // Right-to-left reading mirrors the reading-order regions; the spatial layout is direction-independent (Mihon's RightAndLeftNavigation).
        if (rightToLeft && layout != TapZoneLayout.RightAndLeft)
        {
            x = 1 - x;
        }

        if (invert is TapZoneInvert.Horizontal or TapZoneInvert.Both)
        {
            x = 1 - x;
        }

        if (invert is TapZoneInvert.Vertical or TapZoneInvert.Both)
        {
            y = 1 - y;
        }

        // 1 - x can land exactly on 1.0 (x was 0): keep it inside the half-open regions.
        x = Math.Min(x, 0.999999);
        y = Math.Min(y, 0.999999);

        var regions = layout switch
        {
            TapZoneLayout.LShaped => LShaped,
            TapZoneLayout.Kindlish => Kindlish,
            TapZoneLayout.Edge => Edge,
            TapZoneLayout.RightAndLeft => RightAndLeft,
            _ => [],
        };

        foreach (var region in regions)
        {
            if (region.Contains(x, y))
            {
                return region.Action;
            }
        }

        return input == TapInput.Touch ? TapAction.Menu : TapAction.None;
    }

    /// <summary>Today's behaviour: touch = three columns (rows in vertical paged mode) with the middle third as the menu area; mouse = two halves. Spatial actions, like the old zones.</summary>
    private static TapAction ResolveDefault(Point point, Size size, TapInput input, bool vertical)
    {
        double pos = vertical ? point.Y : point.X;
        double extent = vertical ? size.Height : size.Width;

        if (input == TapInput.Mouse)
        {
            return pos >= extent / 2 ? TapAction.Right : TapAction.Left;
        }

        double third = extent / 3;
        if (pos < third)
        {
            return TapAction.Left;
        }

        return pos > third * 2 ? TapAction.Right : TapAction.Menu;
    }
}
