using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The built-in looks (docs/superpowers/specs/2026-11-06-looks-and-presets-design.md).</summary>
public class LookPresetsTests
{
    [Fact]
    public void ThePresets_HaveTheAgreedValues()
    {
        Assert.Equal(new LookValues(35, 0, SidebarStyle.Tint, 0, ThemeService.GlowTierSubtle), LookPresets.Quiet.Values);
        Assert.Equal(new LookValues(60, 2, SidebarStyle.Pill, 1, ThemeService.GlowTierNormal), LookPresets.Collector.Values);
        Assert.Equal(new LookValues(80, 2, SidebarStyle.Pill, 2, ThemeService.GlowTierVivid), LookPresets.Vivid.Values);
        Assert.Equal(new[] { "Quiet", "Collector", "Vivid" }, LookPresets.All.Select(p => p.Name));
    }

    [Fact]
    public void Match_FindsEachPresetExactly()
    {
        foreach (var preset in LookPresets.All)
        {
            Assert.Same(preset, LookPresets.Match(preset.Values));
            Assert.Same(preset, LookPresets.Find(preset.Name));
        }
    }

    [Fact]
    public void TheThemeDefault_AndMixes_AreCustom()
    {
        Assert.Null(LookPresets.Match(LookPresets.ThemeDefault));
        Assert.Null(LookPresets.Match(LookPresets.Quiet.Values with { Corners = 36 }));
        Assert.Null(LookPresets.Match(LookPresets.Vivid.Values with { Corners = null }));
        Assert.Null(LookPresets.Find("Loud"));
    }

    [Fact]
    public void APreset_CarriesOnlyTheFiveLookValues()
    {
        // No accent, font or text-size field may ever be added to a look (personal and accessibility choices).
        var names = typeof(LookValues).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "CardElevation", "Corners", "FocusThickness", "RingStrength", "Sidebar" }, names);
    }
}
