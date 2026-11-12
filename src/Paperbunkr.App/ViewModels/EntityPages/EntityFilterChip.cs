using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels.EntityPages;

/// <summary>A role chip (creator) or imprint chip (publisher) in the entity page header (redesign §4.3, §4.4). <see cref="Value"/> is the raw
/// role / imprint, null for "All".</summary>
public sealed partial class EntityFilterChip : ObservableObject
{
    public EntityFilterChip(string? value, string label, Action<EntityFilterChip> choose)
    {
        Value = value;
        Label = label;
        ChooseCommand = new RelayCommand(() => choose(this));
    }

    public string? Value { get; }
    public string Label { get; }
    public ICommand ChooseCommand { get; }
    [ObservableProperty] private bool _isSelected;
}

public enum AppearanceSort { ReadingOrder, Newest, Series }
