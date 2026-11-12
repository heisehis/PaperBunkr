using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The five values a look preset sets (docs/superpowers/specs/2026-11-06-looks-and-presets-design.md). Deliberately nothing else: the accent, the fonts and the text size are personal
/// and accessibility choices a preset must never overwrite. <see cref="Corners"/> is null when the radius overrides are off the slider's curve (edited in Customize).
/// </summary>
public sealed record LookValues(int? Corners, int CardElevation, SidebarStyle Sidebar, int FocusThickness, int RingStrength);

/// <summary>One built-in look.</summary>
public sealed record LookPreset(string Name, string Description, LookValues Values);

/// <summary>The built-in looks and the matching that tells which one (if any) the current values are.</summary>
public static class LookPresets
{
    public static LookPreset Quiet { get; } = new("Quiet", "Flatter corners, flat cards, tint sidebar, thin and subtle ring",
        new LookValues(35, 0, SidebarStyle.Tint, 0, ThemeService.GlowTierSubtle));

    public static LookPreset Collector { get; } = new("Collector", "Slightly rounder corners, lifted cards, pill sidebar, normal ring",
        new LookValues(60, 2, SidebarStyle.Pill, 1, ThemeService.GlowTierNormal));

    public static LookPreset Vivid { get; } = new("Vivid", "Round corners, lifted cards, pill sidebar, thick and vivid ring",
        new LookValues(80, 2, SidebarStyle.Pill, 2, ThemeService.GlowTierVivid));

    public static IReadOnlyList<LookPreset> All { get; } = new[] { Quiet, Collector, Vivid };

    /// <summary>The theme's own look: what Reset personalization gives. Not a preset; it reads as "Custom".</summary>
    public static LookValues ThemeDefault { get; } = new(PersonalizationMath.DefaultCorners, 1, SidebarStyle.Standard, 1, ThemeService.GlowTierNormal);

    /// <summary>The preset whose five values these are, or null ("Custom").</summary>
    public static LookPreset? Match(LookValues values) => All.FirstOrDefault(p => p.Values == values);

    public static LookPreset? Find(string? name) => All.FirstOrDefault(p => p.Name == name);
}
