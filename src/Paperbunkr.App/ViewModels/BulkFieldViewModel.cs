using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Runtime state for one bulk-edit field row (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
/// §4) - one instance per <see cref="BulkFieldDescriptor"/> per edit session, unlike the
/// descriptor itself which is a single static, issue-independent definition.
/// </summary>
public partial class BulkFieldViewModel : ObservableObject
{
    public BulkFieldDescriptor Descriptor { get; }

    public BulkFieldViewModel(BulkFieldDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public string Label => Descriptor.Label;
    public bool IsTextKind => Descriptor.Kind == FieldKind.Text;
    public bool IsBooleanKind => Descriptor.Kind == FieldKind.Boolean;
    public bool IsRatingKind => Descriptor.Kind == FieldKind.Rating;
    public bool IsEnumKind => Descriptor.Kind == FieldKind.Enum;

    /// <summary>Candidate values for an <see cref="IsEnumKind"/> row's flyout (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §1).</summary>
    public IReadOnlyList<string> Options => Descriptor.Options ?? [];

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isStaged;

    /// <summary>
    /// Almost always true - the one exception is the bespoke Reading Direction row
    /// (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §2), whose
    /// visibility depends on a *different* row's (Content Type's) live value. Since this field
    /// itself has no reference to sibling rows or the parent ViewModel, the parent
    /// (BulkIssuePropertiesScreenViewModel) pushes the computed visibility in here directly rather
    /// than this class reaching back out - keeps the dependency one-directional.
    /// </summary>
    [ObservableProperty]
    private bool _isRowVisible = true;

    /// <summary>List fields only - the intersection shown at Load time, diffed against the edited <see cref="Value"/> on Save.</summary>
    internal HashSet<string> OriginalTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Auto-stage on any edit (typing, toggling <see cref="BoolValue"/>, clicking a star) - matches CE's TextChanged-driven auto-check.</summary>
    partial void OnValueChanged(string value)
    {
        IsStaged = true;
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(Star1));
        OnPropertyChanged(nameof(Star2));
        OnPropertyChanged(nameof(Star3));
        OnPropertyChanged(nameof(Star4));
        OnPropertyChanged(nameof(Star5));
    }

    public bool BoolValue
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }

    public bool Star1 => RatingAtLeast(1);
    public bool Star2 => RatingAtLeast(2);
    public bool Star3 => RatingAtLeast(3);
    public bool Star4 => RatingAtLeast(4);
    public bool Star5 => RatingAtLeast(5);

    private bool RatingAtLeast(int star) => int.TryParse(Value, out int rating) && rating >= star;

    /// <summary>Toggle-to-clear: clicking the currently-set star unrates it, same as the single-book editor.</summary>
    private static string ToggleStar(string currentValue, int star) =>
        int.TryParse(currentValue, out int rating) && rating == star ? string.Empty : star.ToString();

    [RelayCommand] private void SetStar1() => Value = ToggleStar(Value, 1);
    [RelayCommand] private void SetStar2() => Value = ToggleStar(Value, 2);
    [RelayCommand] private void SetStar3() => Value = ToggleStar(Value, 3);
    [RelayCommand] private void SetStar4() => Value = ToggleStar(Value, 4);
    [RelayCommand] private void SetStar5() => Value = ToggleStar(Value, 5);

    /// <summary>Invoked by an <see cref="IsEnumKind"/> row's flyout options (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §1).</summary>
    [RelayCommand] private void SetValue(string value) => Value = value;
}
