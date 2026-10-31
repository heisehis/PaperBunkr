using System;
using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds a Reading List member row's right-click menu as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - a new menu, this screen had
/// none before. Entirely built from <see cref="ReadingListItemRowViewModel"/>'s own existing per-row
/// commands - <c>OpenCommand</c>, a single <c>ToggleReadCommand</c> (not separate Mark Read/Unread -
/// the row only has one toggle, corrected from the design doc's initial guess), <c>SetRoleCommand</c>
/// (one child per <see cref="EventMembershipRoleOption"/>, radio-checked against
/// <see cref="ReadingListItemRowViewModel.SelectedRole"/>), and <c>RemoveCommand</c>.
/// </summary>
public sealed class ReadingListMemberContextMenuBuilder
{
    private readonly Func<IReadOnlyList<Command>> _pluginCommands;
    private readonly Func<Command, Issue, System.Threading.Tasks.Task> _runPlugin;

    /// <summary>
    /// <paramref name="pluginCommands"/>/<paramref name="runPlugin"/> default to the app's plugin host: Library commands that list
    /// <c>readinglist</c> in their manifest <c>menus</c> (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-
    /// design.md §E) run on the row's issue. Tests pass their own.
    /// </summary>
    public ReadingListMemberContextMenuBuilder(Func<IReadOnlyList<Command>>? pluginCommands = null, Func<Command, Issue, System.Threading.Tasks.Task>? runPlugin = null)
    {
        _pluginCommands = pluginCommands ?? (() => PluginHostService.Current?.GetLibraryCommands()
            .Where(c => c.ShowsInMenu(Command.MenuNames.ReadingList)).ToList() ?? (IReadOnlyList<Command>)Array.Empty<Command>());
        _runPlugin = runPlugin ?? ((command, issue) => PluginHostService.Current?.RunLibraryCommandAsync(command, new[] { issue }) ?? System.Threading.Tasks.Task.CompletedTask);
    }

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        ReadingListItemRowViewModel row => BuildRowMenu(row),
        _ => null,
    };

    private IReadOnlyList<ContextMenuEntry> BuildRowMenu(ReadingListItemRowViewModel row)
    {
        var entries = BuildBaseRowMenu(row).ToList();
        var commands = row.Item.Issue is null ? Array.Empty<Command>() : _pluginCommands();
        if (commands.Count > 0 && ContextMenuEntry.SubMenu("Plugins", commands.Select(c =>
                ContextMenuEntry.Item(c.Name, new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => _runPlugin(c, row.Item.Issue!)))), Symbol.PuzzlePiece) is { } plugins)
        {
            entries.Insert(entries.Count - 2, plugins);   // above the separator and "Remove from List"
        }

        return entries;
    }

    private static IReadOnlyList<ContextMenuEntry> BuildBaseRowMenu(ReadingListItemRowViewModel row) => new[]
    {
        ContextMenuEntry.Item("Open", row.OpenCommand),
        ContextMenuEntry.Item(row.IsRead ? "Mark as Unread" : "Mark as Read", row.ToggleReadCommand),
        // RoleOptions.All is never empty, so SubMenu here never actually returns null - the ! isn't
        // hiding a real edge case, just satisfying the same nullable-return shape ContextMenuEntry.SubMenu
        // always has (same tolerated pattern already in BooksContextMenuBuilder).
        ContextMenuEntry.SubMenu("Set Role", RoleChildren(row))!,
        ContextMenuEntry.Item("Add a Note", row.BeginNoteCommand),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Move Up", row.MoveUpCommand),
        ContextMenuEntry.Item("Move Down", row.MoveDownCommand),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Remove from List", row.RemoveCommand, isDanger: true),
    };

    private static IEnumerable<ContextMenuEntry?> RoleChildren(ReadingListItemRowViewModel row) =>
        ReadingListItemRowViewModel.RoleOptions.Select(option =>
            ContextMenuEntry.Item(option.Label, row.SetRoleCommand, option, isChecked: row.SelectedRole == option.Role));
}
