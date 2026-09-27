using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Tabs inside the Reader and Organize &amp; Scrape sections (docs/superpowers/specs/2026-09-26-preferences-reader-organize-tabs-design.md)
/// and the reveal step that makes a search hit or deep link land on the right one (also for About's tabs, declared in
/// <c>PreferencesScreenViewModel.About.cs</c>). Library Health keeps its own tab state
/// (<c>PreferencesScreenViewModel.LibraryHealthTabs.cs</c>).
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>Tab keys of the Reader section; the same strings are <see cref="PreferenceIndexEntry.SubTab"/> values and the panels' keys.</summary>
    public static class ReaderTabKeys
    {
        public const string Pages = "pages";
        public const string Controls = "controls";
        public const string Image = "image";
        public const string Comfort = "comfort";
        public const string Profiles = "profiles";
    }

    /// <summary>The Reader section's tabs, in display order. In memory only: reopening Preferences after a restart starts on Pages.</summary>
    public SettingsTabs ReaderTabs { get; } = new(new SettingsTabItem[]
    {
        new(ReaderTabKeys.Pages, "Pages"),
        new(ReaderTabKeys.Controls, "Controls"),
        new(ReaderTabKeys.Image, "Image"),
        new(ReaderTabKeys.Comfort, "Comfort"),
        new(ReaderTabKeys.Profiles, "Profiles"),
    });

    /// <summary>
    /// Selects the tab that holds <paramref name="anchorKey"/> so its group is laid out before the shell scrolls to it. Anchors with
    /// no <see cref="PreferenceIndexEntry.SubTab"/> (every other section, and Library Health, which has its own reveal) are left alone.
    /// </summary>
    private void RevealSubTab(string anchorKey)
    {
        var entry = PreferenceIndex.Find(anchorKey);
        if (entry?.SubTab is not { } tab)
        {
            return;
        }

        switch (entry.Section)
        {
            case PreferencesSection.Reader:
                ReaderTabs.Select(tab);
                break;
            case PreferencesSection.OrganizeScrape:
                OrganizeScrape.Tabs.Select(tab);
                break;
            case PreferencesSection.About:
                AboutTabs.Select(tab);
                break;
        }
    }
}
