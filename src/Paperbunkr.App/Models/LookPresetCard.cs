using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Models;

/// <summary>
/// One preset card in Personalize (docs/superpowers/specs/2026-11-06-looks-and-presets-design.md): the name, a one-line description and a tiny sample drawn from the
/// preset's own values (its corner, card shadow and ring), so a card shows its look before it is applied.
/// </summary>
public sealed partial class LookPresetCard : ObservableObject
{
    public LookPresetCard(LookPreset preset)
    {
        Preset = preset;
        var (radius, _, _) = PersonalizationMath.CornersToRadii(preset.Values.Corners ?? PersonalizationMath.DefaultCorners);
        SampleCorner = new CornerRadius(radius);
        var glow = Color.Parse("#E0995A");
        var shadows = ThemeService.BuildCardShadows(glow, preset.Values.CardElevation);
        SampleShadow = shadows["PbPosterShadow"];
        SampleRing = ThemeService.BuildGlowRing(glow, preset.Values.RingStrength, PersonalizationMath.FocusRingStroke(preset.Values.FocusThickness) / 2.0);
    }

    public LookPreset Preset { get; }

    public string Name => Preset.Name;

    public string Description => Preset.Description;

    public string AutomationName => $"{Preset.Name} look: {Preset.Description}";

    public CornerRadius SampleCorner { get; }

    public BoxShadows SampleShadow { get; }

    public BoxShadows SampleRing { get; }

    [ObservableProperty]
    private bool _isSelected;
}
