using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>
/// The isolated sections of the reworked Preferences screen (docs/superpowers/specs/
/// 2026-08-28-preferences-rework-design.md). Replaces the old free-text <c>ActiveTab</c> string.
/// Enum order is the sidebar order.
/// </summary>
public enum PreferencesSection
{
    General,
    Appearance,
    Library,
    Reader,
    KeyboardShortcuts,
    Connections,
    Plugins,
    Advanced,
}

/// <summary>Sidebar ordering + display labels for <see cref="PreferencesSection"/>.</summary>
public static class PreferencesSectionMeta
{
    public static IReadOnlyList<PreferencesSection> Order { get; } = Enum.GetValues<PreferencesSection>();

    public static string Label(PreferencesSection section) => section switch
    {
        PreferencesSection.KeyboardShortcuts => "Keyboard Shortcuts",
        _ => section.ToString(),
    };
}
