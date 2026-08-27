using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One Genre/Tags chip on the Detail screen (docs/superpowers/specs/2026-08-23-weighted-
/// categorized-tags-design.md). Left-click runs the existing Library search for this value;
/// right-click's ContextMenu is a Weight-only quick-picker - Category is only editable from the
/// Issue Properties Editor. <see cref="CanReweight"/> is false in series-aggregated view (no
/// single Issue row a reweight could write to) - the reweight callback is only supplied when
/// <see cref="DetailPillsViewModel"/> is showing one issue's own tags.
/// </summary>
public partial class TagPillViewModel : ObservableObject
{
    private readonly Action<string> _search;
    private readonly Action<IssueTagWeight>? _reweight;

    public TagPillViewModel(string value, string? category, IssueTagWeight weight, Action<string> search, Action<IssueTagWeight>? reweight)
    {
        Value = value;
        Category = category ?? "Uncategorized";
        _weight = weight;
        _search = search;
        _reweight = reweight;
    }

    public string Value { get; }

    public string Category { get; }

    [ObservableProperty]
    private IssueTagWeight _weight;

    public bool CanReweight => _reweight is not null;

    /// <summary>Core/Defining render bold, Recurrent/Incidental/Unset stay the pre-existing SemiBold weight.</summary>
    public FontWeight TextWeight => Weight is IssueTagWeight.Core or IssueTagWeight.Defining ? FontWeight.Bold : FontWeight.SemiBold;

    /// <summary>Unset matches the pre-existing plain-chip look (full opacity) - never inferred as anything else.</summary>
    public double WeightOpacity => Weight switch
    {
        IssueTagWeight.Core => 1.0,
        IssueTagWeight.Defining => 0.9,
        IssueTagWeight.Recurrent => 0.8,
        IssueTagWeight.Incidental => 0.55,
        _ => 1.0,
    };

    partial void OnWeightChanged(IssueTagWeight value)
    {
        OnPropertyChanged(nameof(TextWeight));
        OnPropertyChanged(nameof(WeightOpacity));
    }

    [RelayCommand]
    private void Search() => _search(Value);

    [RelayCommand]
    private void SetWeight(IssueTagWeight weight)
    {
        if (_reweight is null)
        {
            return;
        }

        _reweight(weight);
        Weight = weight;
    }
}
