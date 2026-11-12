using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels.EntityPages;

/// <summary>One tab of the entity page strip (entity pages redesign §3). A tab with nothing in it is hidden.</summary>
public sealed partial class EntityPageTab : ObservableObject
{
    public EntityPageTab(string id, string label, Action<EntityPageTab> select)
    {
        Id = id;
        Label = label;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Id { get; }
    public string Label { get; }
    public ICommand SelectCommand { get; }

    /// <summary>Shown after the label ("Appearances 32"); null for tabs that carry no count (Overview, Career).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Text))] private int? _count;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isSelected;

    public string Text => Count is int count ? $"{Label} {count:N0}" : Label;
}
