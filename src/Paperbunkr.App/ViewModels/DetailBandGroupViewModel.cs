using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One tamed metadata group in the <see cref="DetailBandViewModel"/> (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md). Shows the first <see cref="Cap"/>
/// chips with an inline "+N more" expander; a group with zero chips is never added to the band at
/// all. The <b>Credits</b> group is special-cased (<see cref="IsCreditsGroup"/>): it renders
/// Writer + Artist only, plus a "full credits ›" link into the Details tab.
/// </summary>
public partial class DetailBandGroupViewModel : ViewModelBase
{
    public const int Cap = 12;

    private readonly IReadOnlyList<TagPillViewModel> _visible;
    private readonly IReadOnlyList<TagPillViewModel> _hidden;

    /// <summary>Regular metadata group (Genres, Teams, Locations, Characters, Tags).</summary>
    public DetailBandGroupViewModel(string label, IReadOnlyList<TagPillViewModel> chips, IReadOnlyList<TagPillViewModel>? hidden = null, string? hiddenNoun = null)
    {
        Label = label;
        _visible = chips;
        _hidden = hidden ?? Array.Empty<TagPillViewModel>();
        HiddenNoun = hiddenNoun ?? "hidden";
        Chips = new ObservableCollection<TagPillViewModel>();
        Rebuild();
    }

    /// <summary>Credits group: Writer + Artist chips, plus a jump to the Details tab.</summary>
    public DetailBandGroupViewModel(IReadOnlyList<TagPillViewModel> writers, IReadOnlyList<TagPillViewModel> artists, Action fullCredits)
    {
        Label = "Credits";
        IsCreditsGroup = true;
        Writers = new ObservableCollection<TagPillViewModel>(writers);
        Artists = new ObservableCollection<TagPillViewModel>(artists);
        _visible = Array.Empty<TagPillViewModel>();
        _hidden = Array.Empty<TagPillViewModel>();
        HiddenNoun = "hidden";
        Chips = new ObservableCollection<TagPillViewModel>();
        FullCreditsCommand = new RelayCommand(fullCredits);
    }

    public string Label { get; }

    public bool IsCreditsGroup { get; }

    /// <summary>Credits group only.</summary>
    public ObservableCollection<TagPillViewModel>? Writers { get; }

    /// <summary>Credits group only.</summary>
    public ObservableCollection<TagPillViewModel>? Artists { get; }

    /// <summary>Credits group only.</summary>
    public IRelayCommand? FullCreditsCommand { get; }

    public bool HasWriters => Writers is { Count: > 0 };
    public bool HasArtists => Artists is { Count: > 0 };

    /// <summary>The chips currently on screen (capped or full, hidden appended when revealed).</summary>
    public ObservableCollection<TagPillViewModel> Chips { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _showHidden;

    /// <summary>Non-null count of junk values filtered out of this group (Tags/CVDB only).</summary>
    public int HiddenCount => _hidden.Count;

    public bool HasHidden => _hidden.Count > 0;

    public string HiddenNoun { get; }

    public string RevealHint => ShowHidden ? $"hide {HiddenCount} import ID{(HiddenCount == 1 ? "" : "s")}" : $"{HiddenCount} {HiddenNoun} — import IDs";

    private IReadOnlyList<TagPillViewModel> Effective => ShowHidden ? _visible.Concat(_hidden).ToList() : _visible;

    public int OverflowCount => Math.Max(0, Effective.Count - Cap);

    public bool HasOverflow => OverflowCount > 0;

    public string MoreLabel => IsExpanded ? "show less" : $"+{OverflowCount} more";

    partial void OnIsExpandedChanged(bool value) => Rebuild();

    partial void OnShowHiddenChanged(bool value)
    {
        Rebuild();
        OnPropertyChanged(nameof(RevealHint));
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void ToggleHidden() => ShowHidden = !ShowHidden;

    private void Rebuild()
    {
        Chips.Clear();
        IEnumerable<TagPillViewModel> take = IsExpanded ? Effective : Effective.Take(Cap);
        foreach (var chip in take)
        {
            Chips.Add(chip);
        }

        OnPropertyChanged(nameof(OverflowCount));
        OnPropertyChanged(nameof(HasOverflow));
        OnPropertyChanged(nameof(MoreLabel));
    }
}
