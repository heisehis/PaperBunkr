using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// Maps a lane/edge colour index to the skin resource it draws with (docs/superpowers/specs/2026-09-25-event-map-design.md
/// §3 "Colors"). Everything is looked up by key at use time - never cached as hex - so a skin swap
/// (<c>ThemeService.ApplySkinResources</c> replaces these resources) shows on the next render.
/// </summary>
public static class EventMapPalette
{
    private static readonly string[] LaneKeys = { "PbChartBlue", "PbChartViolet", "PbSuccess", "PbBadge", "PbDanger", "PbAccentText" };

    /// <summary>Resource key prefix: append "Color" or "Brush".</summary>
    public static string KeyPrefix(int colorIndex) => colorIndex switch
    {
        EventMapLayout.TrunkColor => "PbAccent",
        EventMapLayout.MutedColor => "PbTextMuted",
        _ => LaneKeys[Math.Abs(colorIndex) % LaneKeys.Length],
    };

    public static string BrushKey(int colorIndex) => KeyPrefix(colorIndex) + "Brush";

    public static Color ResolveColor(StyledElement host, int colorIndex) => ResolveColor(host, KeyPrefix(colorIndex) + "Color");

    public static Color ResolveColor(StyledElement host, string colorKey)
    {
        if (host.TryFindResource(colorKey, host.ActualThemeVariant, out object? value))
        {
            switch (value)
            {
                case Color color:
                    return color;
                case ISolidColorBrush brush:
                    return brush.Color;
            }
        }

        return Colors.Gray;
    }
}
