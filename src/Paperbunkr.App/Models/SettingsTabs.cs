using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>One tab in a Preferences section's tab strip. <see cref="Key"/> is what panels and search entries refer to.</summary>
public sealed record SettingsTabItem(string Key, string Title);

/// <summary>
/// The tab state behind a Preferences section split into tabs (docs/superpowers/specs/2026-09-26-preferences-reader-organize-tabs-design.md).
/// A section owns one of these, binds a <c>SettingsTabStrip</c> to it, and shows each panel when <see cref="SelectedKey"/> equals the
/// panel's key. Held in memory only: it is not persisted, so Preferences reopens on the first tab after a restart.
/// </summary>
public sealed partial class SettingsTabs : ObservableObject
{
    public SettingsTabs(IEnumerable<SettingsTabItem> items)
    {
        Items = items.ToList();
        if (Items.Count == 0)
        {
            throw new ArgumentException("A tab strip needs at least one tab.", nameof(items));
        }

        _selected = Items[0];
    }

    public IReadOnlyList<SettingsTabItem> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedKey))]
    private SettingsTabItem _selected;

    public string SelectedKey => Selected.Key;

    public bool Contains(string? key) => key is not null && Items.Any(t => t.Key == key);

    /// <summary>Selects the tab with this key. Returns false (and changes nothing) for an unknown key.</summary>
    public bool Select(string? key)
    {
        var tab = key is null ? null : Items.FirstOrDefault(t => t.Key == key);
        if (tab is null)
        {
            return false;
        }

        Selected = tab;
        return true;
    }
}
