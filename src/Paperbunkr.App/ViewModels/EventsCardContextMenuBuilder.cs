using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the Events &amp; Continuity sidebar's row right-click menu as plain
/// <see cref="ContextMenuEntry"/> data (docs/superpowers/specs/2026-08-31-keyboard-operability-
/// design.md) - a new menu, this sidebar had none before (only its own inline Open/Delete buttons).
/// Lives on <see cref="MainViewModel"/>, not <see cref="EventsScreenViewModel"/> - the sidebar itself
/// is declared in <c>MainWindow.axaml</c> with <see cref="MainViewModel"/> as its <c>DataContext</c>
/// (confirmed by reading the file: the Events/Continuities <c>ItemsControl</c>s bind
/// <c>Events.SelectEventCommand</c>/<c>Events.SelectContinuityCommand</c> through
/// <c>#StoryEvents.((vm:MainViewModel)DataContext)</c>-style long-form bindings), so
/// <see cref="MainViewModel"/> is the only place that already has both the select and edit-dialog
/// entry points needed together.
/// </summary>
public sealed class EventsCardContextMenuBuilder
{
    private readonly MainViewModel _vm;

    public EventsCardContextMenuBuilder(MainViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        StoryEventSummary row => BuildEventMenu(row),
        ContinuitySummary row => BuildContinuityMenu(row),
        _ => null,
    };

    private IReadOnlyList<ContextMenuEntry> BuildEventMenu(StoryEventSummary row)
    {
        var entries = new List<ContextMenuEntry>
        {
            ContextMenuEntry.Item("Open", _vm.Events.SelectEventCommand, row),
            ContextMenuEntry.Item("Edit details", _vm.EditEventFromContextMenuCommand, row),
        };

        entries.Add(ContextMenuEntry.Separator);
        entries.Add(ContextMenuEntry.Item(row.DeleteConfirm.Label, row.DeleteConfirm.TriggerCommand, isDanger: true));
        return entries;
    }

    private IReadOnlyList<ContextMenuEntry> BuildContinuityMenu(ContinuitySummary row)
    {
        var entries = new List<ContextMenuEntry>
        {
            ContextMenuEntry.Item("Open", _vm.Events.SelectContinuityCommand, row),
            ContextMenuEntry.Item("Edit details", _vm.EditContinuityFromContextMenuCommand, row),
        };

        if (row.DeleteConfirm is { } deleteConfirm)
        {
            entries.Add(ContextMenuEntry.Separator);
            entries.Add(ContextMenuEntry.Item(deleteConfirm.Label, deleteConfirm.TriggerCommand, isDanger: true));
        }

        return entries;
    }
}
