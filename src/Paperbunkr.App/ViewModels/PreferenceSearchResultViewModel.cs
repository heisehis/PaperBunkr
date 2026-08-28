using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>A single hit in the Preferences search box (docs/superpowers/specs/
/// 2026-08-28-preferences-rework-design.md "Search"). Wraps one <see cref="PreferenceIndexEntry"/>.</summary>
public sealed class PreferenceSearchResultViewModel
{
    public PreferenceSearchResultViewModel(PreferenceIndexEntry entry)
    {
        Entry = entry;
    }

    public PreferenceIndexEntry Entry { get; }

    public PreferencesSection Section => Entry.Section;

    public string AnchorKey => Entry.AnchorKey;

    public string Title => Entry.Title;

    public string GroupTitle => Entry.GroupTitle;

    public string SectionLabel => PreferencesSectionMeta.Label(Entry.Section);
}
