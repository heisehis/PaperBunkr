using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Per-series accent for a Detail screen (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #9). Overrides the
/// <c>PbAccent*</c> tokens on the SCREEN's own <see cref="Control.Resources"/> - so only that screen's subtree re-tints; the nav rail,
/// toolbar and every other screen keep the skin accent - using the cover's dominant colour, contrast-adjusted against the current
/// background with the same helper the user-picked accent override uses (text stays &gt;= 4.5:1). No palette, or the setting off,
/// removes the overrides and the skin accent shows through.
/// </summary>
public static class DetailAccentScope
{
    private static readonly string[] Keys =
    {
        "PbAccentColor", "PbAccentBrush", "PbAccentTextColor", "PbAccentTextBrush", "PbAccentSoftColor", "PbAccentSoftBrush",
    };

    /// <summary>Applies (or clears) the scoped accent for <paramref name="cover"/>. Returns the colour used, or null when cleared.</summary>
    public static Color? Apply(Control scopeRoot, Bitmap? cover, bool enabled)
        => Apply(scopeRoot, enabled ? CoverPalette.FromBitmap(cover) : new List<Color>());

    public static Color? Apply(Control scopeRoot, IReadOnlyList<Color> palette)
    {
        foreach (string key in Keys)
        {
            scopeRoot.Resources.Remove(key);
        }

        if (palette.Count == 0)
        {
            return null;
        }

        Color bg = scopeRoot.TryFindResource("PbBgColor", out object? bgValue) && bgValue is Color c ? c : Colors.Black;
        Color accent = palette[0];
        Color text = ThemeService.AdjustForContrast(accent, bg, targetRatio: 4.5);
        Color soft = Color.FromArgb(0x29, accent.R, accent.G, accent.B);

        scopeRoot.Resources["PbAccentColor"] = accent;
        scopeRoot.Resources["PbAccentBrush"] = new SolidColorBrush(accent).ToImmutable();
        scopeRoot.Resources["PbAccentTextColor"] = text;
        scopeRoot.Resources["PbAccentTextBrush"] = new SolidColorBrush(text).ToImmutable();
        scopeRoot.Resources["PbAccentSoftColor"] = soft;
        scopeRoot.Resources["PbAccentSoftBrush"] = new SolidColorBrush(soft).ToImmutable();
        return accent;
    }
}
