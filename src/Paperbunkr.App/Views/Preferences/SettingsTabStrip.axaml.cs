using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views.Preferences;

/// <summary>
/// The tab strip under a Preferences section's title (docs/superpowers/specs/2026-09-26-preferences-reader-organize-tabs-design.md).
/// Bind <see cref="Tabs"/> to the section's <see cref="SettingsTabs"/>; the strip is a restyled <see cref="TabStrip"/>, so it exposes
/// tab roles to screen readers and moves between tabs with the arrow keys. Selection is pushed one way in each direction by hand
/// (never a TwoWay binding), and the model-to-strip direction is fenced off from the strip-to-model one, because a TabStrip
/// always keeps something selected and would otherwise overwrite the model with its first tab.
/// </summary>
public partial class SettingsTabStrip : UserControl
{
    public static readonly StyledProperty<SettingsTabs?> TabsProperty =
        AvaloniaProperty.Register<SettingsTabStrip, SettingsTabs?>(nameof(Tabs));

    private SettingsTabs? _hooked;
    private bool _syncing;

    public SettingsTabs? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    public SettingsTabStrip()
    {
        InitializeComponent();
        Strip.SelectionChanged += OnStripSelectionChanged;
    }

    private TabStrip Strip => this.FindControl<TabStrip>("PART_Strip")!;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TabsProperty)
        {
            return;
        }

        if (_hooked is not null)
        {
            _hooked.PropertyChanged -= OnTabsPropertyChanged;
        }

        _hooked = Tabs;
        if (_hooked is not null)
        {
            _hooked.PropertyChanged += OnTabsPropertyChanged;
        }

        // A TabStrip always keeps something selected (it re-selects the first tab when its selection is cleared), so handing it a new
        // item list may select a tab on its own. That is the strip catching up with the model, never the user choosing, so it must
        // not be written back over the model's selection. Defensive: the headless tests only pin the outcome, not this ordering.
        PushModelSelection(setItems: true);
    }

    private void OnTabsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsTabs.Selected) && !ReferenceEquals(Strip.SelectedItem, _hooked?.Selected))
        {
            PushModelSelection(setItems: false);
        }
    }

    private void PushModelSelection(bool setItems)
    {
        _syncing = true;
        try
        {
            if (setItems)
            {
                Strip.ItemsSource = _hooked?.Items;
            }

            Strip.SelectedItem = _hooked?.Selected;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnStripSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && _hooked is not null && Strip.SelectedItem is SettingsTabItem item)
        {
            _hooked.Select(item.Key);
        }
    }
}
