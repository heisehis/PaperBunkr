using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>
/// One searchable entry per settings group card (docs/superpowers/specs/
/// 2026-08-28-preferences-rework-design.md "Search"). <see cref="Keywords"/> carries the labels of
/// the individual controls inside the group so a query like "double page" still resolves to the
/// Reader → Display card. <see cref="AnchorKey"/> matches the <c>Tag</c> on that group's
/// <c>Border</c> in the section's .axaml, which the shell scrolls into view.
/// </summary>
public sealed record PreferenceIndexEntry(
    PreferencesSection Section,
    string GroupTitle,
    string Title,
    IReadOnlyList<string> Keywords,
    string AnchorKey);

/// <summary>
/// The static catalog behind the Preferences search box. Hand-maintained; kept honest by
/// <c>PreferenceIndexTests</c>, which asserts every <see cref="PreferenceIndexEntry.AnchorKey"/>
/// resolves to a real <c>Border.Tag</c> in its section control.
/// </summary>
public static class PreferenceIndex
{
    public static IReadOnlyList<PreferenceIndexEntry> Entries { get; } = new PreferenceIndexEntry[]
    {
        new(PreferencesSection.General, "Reading", "Reading",
            new[] { "resume", "left off", "last page", "auto advance", "next issue", "open next" },
            "general.reading"),
        new(PreferencesSection.General, "Window", "Window",
            new[] { "minimize", "tray", "close to tray", "system tray" },
            "general.window"),

        new(PreferencesSection.Appearance, "Skins", "Skin",
            new[] { "skin", "theme", "colors", "palette", "windows 11", "evolved amber" },
            "appearance.skin"),
        new(PreferencesSection.Appearance, "Install Skin", "Install Skin",
            new[] { "install skin", "crpck", "browse skin", "skins folder" },
            "appearance.installSkin"),
        new(PreferencesSection.Appearance, "Font", "Font",
            new[] { "font", "typeface", "font family" },
            "appearance.font"),
        new(PreferencesSection.Appearance, "Motion", "Motion",
            new[] { "motion", "reduce motion", "animation", "transitions" },
            "appearance.motion"),
        new(PreferencesSection.Appearance, "Developer", "Developer",
            new[] { "developer", "design showcase", "debug" },
            "appearance.developer"),

        new(PreferencesSection.Library, "Comic Library Folders", "Comic Library Folders",
            new[] { "comic folder", "watched folder", "scan", "generate covers", "sync metadata", "watch for changes" },
            "library.comicFolders"),
        new(PreferencesSection.Library, "Book Folders", "Book Folders",
            new[] { "book folder", "novel", "epub", "pdf", "scan books" },
            "library.bookFolders"),
        new(PreferencesSection.Library, "Migrate from ComicRack CE", "Migrate from ComicRack CE",
            new[] { "migrate", "comicrack", "ce", "import library" },
            "library.migration"),
        new(PreferencesSection.Library, "Virtual Tags", "Virtual Tags",
            new[] { "virtual tag", "caption format", "computed tag" },
            "library.virtualTags"),

        new(PreferencesSection.Reader, "Right to Left", "Right to Left",
            new[] { "right to left", "rtl", "manga", "page turn direction", "reverse" },
            "reader.rtl"),
        new(PreferencesSection.Reader, "Display", "Display",
            new[] { "high quality", "fit mode", "auto rotate", "double page", "spread", "page transition", "transition speed" },
            "reader.display"),
        new(PreferencesSection.Reader, "Zoom & Navigation", "Zoom & Navigation",
            new[] { "reset zoom", "mouse wheel", "scroll speed", "zoom" },
            "reader.zoomNav"),
        new(PreferencesSection.Reader, "Image Adjustment", "Image Adjustment",
            new[] { "brightness", "contrast", "saturation", "gamma", "image adjustment" },
            "reader.imageAdjust"),
        new(PreferencesSection.Reader, "Background & Margin", "Background & Margin",
            new[] { "canvas background", "background color", "page margin", "margin width" },
            "reader.background"),

        new(PreferencesSection.KeyboardShortcuts, "Import / Export", "Import / Export Layout",
            new[] { "import layout", "export layout", "shortcut layout", "keybindings file" },
            "shortcuts.io"),
        new(PreferencesSection.KeyboardShortcuts, "Navigation", "Navigation Shortcuts",
            new[] { "pan", "scroll", "page up", "page down", "home", "end", "next page", "previous page" },
            "shortcuts.navigation"),
        new(PreferencesSection.KeyboardShortcuts, "Zoom & Fit", "Zoom & Fit Shortcuts",
            new[] { "zoom in", "zoom out", "fit width", "fit height", "best fit", "actual size" },
            "shortcuts.zoomFit"),
        new(PreferencesSection.KeyboardShortcuts, "Display", "Display Shortcuts",
            new[] { "fullscreen", "rotate", "rotate clockwise", "rotate counter clockwise" },
            "shortcuts.display"),

        new(PreferencesSection.Connections, "Reading List Sources", "Reading List Sources",
            new[] { "comicvine", "api key", "metron", "reading list source", "arc lookup" },
            "connections.metadataSources"),
        new(PreferencesSection.Connections, "Trackers", "Trackers",
            new[] { "tracker", "anilist", "myanimelist", "mal", "shikimori", "bangumi", "mangabaka", "connect", "token" },
            "connections.trackers"),

        new(PreferencesSection.Advanced, "Rendering", "Rendering",
            new[] { "graphics backend", "gpu", "software renderer", "opengl", "angle", "hardware acceleration" },
            "advanced.rendering"),
        new(PreferencesSection.Advanced, "File Association", "File Association",
            new[] { "file association", "cbz", "cbr", "cb7", "cbl", "default app", "open with" },
            "advanced.fileAssociation"),
        new(PreferencesSection.Advanced, "Backup Manager", "Backup Manager",
            new[] { "backup", "restore", "backup location", "backups to keep" },
            "advanced.backup"),

        new(PreferencesSection.About, "Updates", "Updates",
            new[] { "update", "check for updates", "auto update", "version", "new version" },
            "about.updates"),
        new(PreferencesSection.About, "Changelog", "Changelog",
            new[] { "changelog", "what's new", "whats new", "release notes", "history" },
            "about.changelog"),
    };
}
