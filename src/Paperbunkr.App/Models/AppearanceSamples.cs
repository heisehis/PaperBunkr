using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>One live swatch in Appearance > Covers: a tile drawn with the hover/focus ring a glow tier produces (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md).</summary>
public sealed partial class GlowTierSample : ObservableObject
{
    public GlowTierSample(int tier, string label, BoxShadows shadow)
    {
        Tier = tier;
        Label = label;
        _shadow = shadow;
    }

    public int Tier { get; }

    public string Label { get; }

    /// <summary>Built by <c>ThemeService.BuildGlowRing</c> from the active theme's glow colour, so the swatch matches what a card will really show.</summary>
    [ObservableProperty]
    private BoxShadows _shadow;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One live swatch in Appearance > Interface: three stacked mini rows spaced the way a density preset spaces real rows.</summary>
public sealed partial class DensitySample : ObservableObject
{
    public DensitySample(int preset, string label, Thickness rowPadding)
    {
        Preset = preset;
        Label = label;
        RowPadding = rowPadding;
    }

    public int Preset { get; }

    public string Label { get; }

    /// <summary>The preset's list row padding, scaled down for the miniature.</summary>
    public Thickness RowPadding { get; }

    [ObservableProperty]
    private bool _isActive;
}
