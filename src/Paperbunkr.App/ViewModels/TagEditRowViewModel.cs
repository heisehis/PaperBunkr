using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One editable Genre/Tags row in the Issue Properties Editor (docs/superpowers/specs/2026-08-23-
/// weighted-categorized-tags-design.md) - Category/Weight edit surface. Adding/removing tag values
/// stays on the existing plain CSV text box; this only edits Category/Weight for values that
/// already exist, matched back onto the post-diff <see cref="IssueTag"/> by <see cref="Value"/> in
/// <see cref="IssuePropertiesScreenViewModel.Save"/>.
/// </summary>
public partial class TagEditRowViewModel : ObservableObject
{
    public TagEditRowViewModel(string value, string? category, IssueTagWeight weight)
    {
        Value = value;
        _category = category ?? string.Empty;
        _weight = weight;
    }

    public string Value { get; }

    [ObservableProperty]
    private string _category;

    [ObservableProperty]
    private IssueTagWeight _weight;

    /// <summary>String view of <see cref="Weight"/> for the <c>SuggestBox</c> weight picker (which
    /// is string-only). Unknown text is ignored, keeping the current value - the picker only ever
    /// offers <see cref="WeightNames"/>.</summary>
    public string WeightText
    {
        get => Weight.ToString();
        set
        {
            if (Enum.TryParse<IssueTagWeight>(value, out var parsed))
            {
                Weight = parsed;
            }
        }
    }

    private static readonly string[] WeightNamesCache = Enum.GetNames<IssueTagWeight>();

    /// <summary>Instance passthrough so a compiled <c>{Binding}</c> in the row template resolves it
    /// (an <c>{x:Static}</c> to the array trips the XAML collection-adder path).</summary>
    public string[] WeightNames => WeightNamesCache;

    partial void OnWeightChanged(IssueTagWeight value) => OnPropertyChanged(nameof(WeightText));
}
