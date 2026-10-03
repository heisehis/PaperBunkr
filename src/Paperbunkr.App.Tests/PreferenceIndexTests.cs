using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Paperbunkr.App.Models;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Drift guard for the Preferences search index (docs/superpowers/specs/
/// 2026-08-28-preferences-rework-design.md "Search"): every <see cref="PreferenceIndexEntry.AnchorKey"/>
/// must correspond to a real <c>Tag="…"</c> on a control in its section's .axaml, every
/// <see cref="PreferencesSection"/> must be represented, and anchor keys must be unique.
/// Reads the section .axaml source files directly off disk (compiled XAML is not kept as an
/// openable asset in this project), so it needs no Avalonia runtime at all.
/// </summary>
public class PreferenceIndexTests
{
    private static readonly IReadOnlyDictionary<PreferencesSection, string> SectionFiles =
        new Dictionary<PreferencesSection, string>
        {
            [PreferencesSection.General] = "GeneralSection.axaml",
            [PreferencesSection.Appearance] = "AppearanceSection.axaml",
            [PreferencesSection.Library] = "LibrarySection.axaml",
            [PreferencesSection.Automation] = "AutomationSection.axaml",
            [PreferencesSection.Reader] = "ReaderSection.axaml",
            [PreferencesSection.KeyboardShortcuts] = "KeyboardShortcutsSection.axaml",
            [PreferencesSection.Connections] = "ConnectionsSection.axaml",
            [PreferencesSection.Acquisition] = "AcquisitionSection.axaml",
            [PreferencesSection.OrganizeScrape] = "OrganizeScrapeSection.axaml",
            [PreferencesSection.Plugins] = "PluginsSection.axaml",
            [PreferencesSection.Advanced] = "AdvancedSection.axaml",
            [PreferencesSection.About] = "AboutSection.axaml",
            [PreferencesSection.Sharing] = "SharingSection.axaml",
        };

    private static string PreferencesViewDir([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "Paperbunkr.App", "Views", "Preferences"));

    private static string ReadSectionAxaml(PreferencesSection section)
        => File.ReadAllText(Path.Combine(PreferencesViewDir(), SectionFiles[section]));

    [Fact]
    public void EverySectionHasAResourceMapping()
    {
        foreach (PreferencesSection section in Enum.GetValues<PreferencesSection>())
        {
            Assert.True(SectionFiles.ContainsKey(section), $"No .axaml mapping for {section}");
        }
    }

    [Fact]
    public void EverySectionHasAtLeastOneIndexEntry()
    {
        foreach (PreferencesSection section in Enum.GetValues<PreferencesSection>())
        {
            if (section == PreferencesSection.Plugins)
            {
                continue; // Plugins hosts an embedded screen with no searchable groups of its own.
            }

            Assert.Contains(PreferenceIndex.Entries, e => e.Section == section);
        }
    }

    [Fact]
    public void AnchorKeysAreUnique()
    {
        var dupes = PreferenceIndex.Entries
            .GroupBy(e => e.AnchorKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(dupes);
    }

    [Fact]
    public void EveryEntryAnchorResolvesToATagInItsSection()
    {
        // Keyboard Shortcuts builds one group per input-service action group (docs/superpowers/specs/2026-10-03-input-service-design.md §9), so a plugin's actions get a section with no XAML
        // change, and tags each with ShortcutGroupViewModel.TagFor. Those anchors are checked against the real action catalog; "shortcuts.io" is a literal Tag like every other.
        var generated = Paperbunkr.App.Services.Input.InputActions.Core.Select(i => i.Group).Distinct().Select(Paperbunkr.App.ViewModels.ShortcutGroupViewModel.TagFor).ToHashSet();
        foreach (var entry in PreferenceIndex.Entries)
        {
            string axaml = ReadSectionAxaml(entry.Section);
            if (entry.Section == PreferencesSection.KeyboardShortcuts && generated.Contains(entry.AnchorKey))
            {
                Assert.Contains("Tag=\"{Binding Tag}\"", axaml);
                continue;
            }

            Assert.True(
                axaml.Contains($"Tag=\"{entry.AnchorKey}\"", StringComparison.Ordinal),
                $"Anchor '{entry.AnchorKey}' ({entry.Section}/{entry.GroupTitle}) has no matching Tag in {SectionFiles[entry.Section]}");
        }
    }

    [Fact]
    public void EveryShortcutActionGroupHasAnIndexEntry_SoSearchCanJumpToIt()
    {
        var indexed = PreferenceIndex.Entries.Select(e => e.AnchorKey).ToHashSet();

        foreach (string group in Paperbunkr.App.Services.Input.InputActions.Core.Select(i => i.Group).Distinct())
        {
            string tag = Paperbunkr.App.ViewModels.ShortcutGroupViewModel.TagFor(group);
            Assert.True(indexed.Contains(tag), $"Shortcut group '{group}' (tag '{tag}') has no PreferenceIndex entry");
        }
    }
}
