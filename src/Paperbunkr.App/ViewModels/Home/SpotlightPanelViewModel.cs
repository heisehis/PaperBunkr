using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels.Home;

/// <summary>One panel of the Home spotlight accordion (docs/superpowers/specs/2026-09-29-home-spotlight-accordion-design.md): a pick
/// plus whether it's the open one.</summary>
public sealed partial class SpotlightPanelViewModel : ObservableObject
{
    public SpotlightPanelViewModel(SpotlightIssueSample sample) => Sample = sample;

    public SpotlightIssueSample Sample { get; }

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Slivers carry no text, so this is how you tell them apart ("Saga · #54").</summary>
    public string Tooltip => string.IsNullOrWhiteSpace(Sample.SeriesName) ? Sample.Title : $"{Sample.SeriesName} · {Sample.Title}";
}
