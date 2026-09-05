using System;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row the Quick Open command palette can surface (docs/superpowers/specs/2026-09-03-quick-open-
/// command-palette-design.md). Content rows carry an <see cref="EntityId"/>; Screen / Action rows
/// carry a <see cref="Key"/> string instead (a screen key like <c>"library"</c>, an action key like
/// <c>"addFolder"</c>) - <c>MainViewModel.ActivateQuickOpenEntry</c> routes on <see cref="Kind"/>
/// plus whichever of the two is set.
/// </summary>
public sealed record QuickOpenEntry(
    QuickOpenKind Kind,
    int? EntityId,
    string Primary,
    string? Secondary,
    string Icon,
    DateTime? RecencyUtc,
    string? Key = null)
{
    /// <summary>The dim right-aligned type label shown on each row ("series", "reading list", …).</summary>
    public string KindLabel => Kind switch
    {
        QuickOpenKind.Series => "series",
        QuickOpenKind.Book => "book",
        QuickOpenKind.Issue => "issue",
        QuickOpenKind.ReadingList => "reading list",
        QuickOpenKind.SmartList => "smart list",
        QuickOpenKind.Collection => "collection",
        QuickOpenKind.StoryEvent => "event",
        QuickOpenKind.Continuity => "continuity",
        QuickOpenKind.Screen => "screen",
        QuickOpenKind.Action => "action",
        QuickOpenKind.PluginCommand => "plugin",
        _ => string.Empty,
    };
}

public enum QuickOpenKind
{
    Series,
    Book,
    Issue,
    ReadingList,
    SmartList,
    Collection,
    StoryEvent,
    Continuity,
    Screen,
    Action,

    /// <summary>Plugin API v2 QuickOpenHtml/QuickOpenUI hook (docs/superpowers/specs/2026-09-05-
    /// plugin-api-v2-remaining-hooks-plan.md §11) - <see cref="QuickOpenEntry.Key"/> is the owning
    /// <c>Command.Key</c>, resolved back to the real <c>Command</c> at activation time.</summary>
    PluginCommand,
}
